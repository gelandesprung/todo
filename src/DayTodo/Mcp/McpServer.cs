using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayTodo.Models;

namespace DayTodo.Mcp;

/// <summary>
/// Minimal, dependency-free MCP server: JSON-RPC 2.0 with initialize / tools/list /
/// tools/call. Works over newline-delimited stdio and over Streamable HTTP.
/// stdout carries protocol messages only - diagnostics always go to stderr.
/// </summary>
public sealed class McpServer
{
    private static readonly string[] ProtocolVersions =
        { "2025-06-18", "2025-03-26", "2024-11-05" };

    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly McpTaskService _tasks;
    private readonly McpNoteService _notes;

    public McpServer(McpTaskService tasks, McpNoteService notes)
    {
        _tasks = tasks;
        _notes = notes;
    }

    /// <summary>Returns a JSON-RPC response, or null for a notification / empty batch.</summary>
    public string? Handle(string message)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(message); }
        catch (JsonException) { return Serialize(Error(null, -32700, "Parse error: invalid JSON")); }

        if (root is JsonArray batch)
        {
            var results = new JsonArray();
            foreach (var item in batch)
            {
                var response = HandleNode(item);
                if (response != null) results.Add(JsonNode.Parse(response));
            }
            return results.Count == 0 ? null : Serialize(results);
        }

        return HandleNode(root);
    }

    private string? HandleNode(JsonNode? node)
    {
        if (node is not JsonObject req)
            return Serialize(Error(null, -32600, "Invalid Request"));

        var hasId = req.ContainsKey("id");
        var id = req.TryGetPropertyValue("id", out var idNode) ? idNode?.DeepClone() : null;
        var method = Str(req, "method");

        if (string.IsNullOrEmpty(method))
            return Serialize(Error(id, -32600, "Invalid Request: missing method"));

        if (method.StartsWith("notifications/", StringComparison.Ordinal) || !hasId) return null;

        var p = req["params"] as JsonObject;
        try
        {
            return method switch
            {
                "initialize" => Serialize(Ok(id, Initialize(p))),
                "ping" => Serialize(Ok(id, new JsonObject())),
                "tools/list" => Serialize(Ok(id, new JsonObject { ["tools"] = Tools() })),
                "tools/call" => Serialize(Ok(id, CallTool(p))),
                "resources/list" => Serialize(Ok(id,
                    new JsonObject { ["resources"] = new JsonArray() })),
                "resources/templates/list" => Serialize(Ok(id,
                    new JsonObject { ["resourceTemplates"] = new JsonArray() })),
                "prompts/list" => Serialize(Ok(id,
                    new JsonObject { ["prompts"] = new JsonArray() })),
                _ => Serialize(Error(id, -32601, "Method not found: " + method))
            };
        }
        catch (Exception ex)
        {
            return Serialize(Error(id, -32603, "Internal error: " + ex.Message));
        }
    }

    private static JsonObject Initialize(JsonObject? p)
    {
        var requested = Str(p, "protocolVersion");
        var version = ProtocolVersions.Contains(requested ?? string.Empty)
            ? requested! : ProtocolVersions[^1];

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "daytodo",
                ["version"] = "1.0.7",
                ["description"] = "DayTodo 日清单：让 AI 客户端管理每日任务与随记"
            },
            ["instructions"] =
                "任务：create_task 创建（默认今天，只能是今天或未来）；list_tasks 查看并按 id 定位；" +
                "complete_task / delete_task 建议传 id，delete_task 是硬删除且不可恢复。" +
                "随记：create_note 只写今天；list_notes 按日期查看；search_notes 全文搜索（可限定日期范围）；" +
                "update_note / delete_note 只能操作今天的随记。" +
                "历史日期与历史随记一律只读；随记引用通过 note_id 关联，可用 list_notes / search_notes 获取。"
        };
    }

    private static JsonArray Tools()
    {
        JsonObject Schema(params (string name, JsonObject prop)[] props)
        {
            var o = new JsonObject { ["type"] = "object" };
            var properties = new JsonObject();
            foreach (var (name, prop) in props) properties[name] = prop;
            o["properties"] = properties;
            return o;
        }

        var createSchema = Schema(
            ("title", new JsonObject { ["type"] = "string", ["description"] = "任务标题，必填" }),
            ("date", new JsonObject
            {
                ["type"] = "string",
                ["description"] = "排期 yyyy-MM-dd，默认今天；只能是今天或未来，历史日期只读"
            }),
            ("priority", new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("low", "normal", "high"),
                ["description"] = "优先级，默认 normal；也接受 高/中/低 或 0/1/2"
            }),
            ("tags", new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["maxItems"] = 4,
                ["description"] = "标签，最多 4 个；也接受逗号分隔的字符串"
            }));
        createSchema["required"] = new JsonArray("title");

        var listSchema = Schema(
            ("date", new JsonObject { ["type"] = "string", ["description"] = "yyyy-MM-dd，默认今天" }),
            ("include_completed", new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "是否包含已完成任务，默认 true"
            }),
            ("all", new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "为 true 时返回全部历史任务，忽略 date，默认 false"
            }));

        var idProperty = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "任务 GUID，可用 list_tasks 获取；推荐用它定位任务"
        };
        var titleProperty = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "任务标题（精确优先，其次包含匹配）；必须唯一，否则返回错误"
        };

        var matchSchema = Schema(
            ("id", (JsonObject)idProperty.DeepClone()),
            ("title", (JsonObject)titleProperty.DeepClone()));

        var completeSchema = Schema(
            ("id", (JsonObject)idProperty.DeepClone()),
            ("title", (JsonObject)titleProperty.DeepClone()),
            ("undo", new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "为 true 时取消完成，默认 false"
            }));
        matchSchema["anyOf"] = new JsonArray(
            new JsonObject { ["required"] = new JsonArray("id") },
            new JsonObject { ["required"] = new JsonArray("title") });
        completeSchema["anyOf"] = new JsonArray(
            new JsonObject { ["required"] = new JsonArray("id") },
            new JsonObject { ["required"] = new JsonArray("title") });

        JsonObject Note(string description) => new()
        {
            ["type"] = "string",
            ["description"] = description
        };

        var noteIdProperty = Note("随记 GUID，可用 list_notes 或 search_notes 获取");
        var noteTextProperty = Note("随记正文，必填，最多 5000 字");
        var noteRefsProperty = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "要引用的随记：id 字符串，或 {id, date?, text?} 对象；目标必须已存在",
            ["items"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(
                    Note("已存在随记的 GUID"),
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = (JsonObject)noteIdProperty.DeepClone(),
                            ["date"] = Note("可选，用于核对目标日期 yyyy-MM-dd"),
                            ["text"] = Note("可选，自定义引用文字；省略则回读目标原文")
                        },
                        ["required"] = new JsonArray("id")
                    })
            }
        };
        var noteLimitProperty = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = "最多返回条数，默认 100，上限 500",
            ["default"] = 100
        };

        var createNoteSchema = Schema(
            ("text", (JsonObject)noteTextProperty.DeepClone()),
            ("references", (JsonObject)noteRefsProperty.DeepClone()));
        createNoteSchema["required"] = new JsonArray("text");

        var listNotesSchema = Schema(
            ("date", Note("yyyy-MM-dd，默认今天；all=true 时忽略")),
            ("all", new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "为 true 时返回全部日期的随记，默认 false"
            }),
            ("limit", (JsonObject)noteLimitProperty.DeepClone()));

        var searchNotesSchema = Schema(
            ("query", Note("搜索关键词，大小写不敏感；同时匹配正文与引用文字")),
            ("from", Note("可选，起始日期 yyyy-MM-dd（含）")),
            ("to", Note("可选，结束日期 yyyy-MM-dd（含）")),
            ("limit", (JsonObject)noteLimitProperty.DeepClone()));
        searchNotesSchema["required"] = new JsonArray("query");

        var updateNoteSchema = Schema(
            ("id", (JsonObject)noteIdProperty.DeepClone()),
            ("text", Note("新的正文；不传则保留原文")),
            ("references", (JsonObject)noteRefsProperty.DeepClone()),
            ("clear_references", new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "为 true 时清空全部引用，默认 false"
            }));
        updateNoteSchema["required"] = new JsonArray("id");

        var deleteNoteSchema = Schema(
            ("id", (JsonObject)noteIdProperty.DeepClone()));
        deleteNoteSchema["required"] = new JsonArray("id");

        return new JsonArray
        {
            new JsonObject
            {
                ["name"] = "create_task",
                ["description"] = "在 DayTodo 日清单里新建一条任务。默认排到今天，可指定未来日期提前安排。",
                ["inputSchema"] = createSchema
            },
            new JsonObject
            {
                ["name"] = "list_tasks",
                ["description"] = "列出某一天（默认今天）可见的任务，返回 id、标题、优先级、标签、完成状态。",
                ["inputSchema"] = listSchema
            },
            new JsonObject
            {
                ["name"] = "complete_task",
                ["description"] = "把任务标记完成，或传 undo=true 取消完成。用 id 或唯一的 title 定位。",
                ["inputSchema"] = completeSchema
            },
            new JsonObject
            {
                ["name"] = "delete_task",
                ["description"] = "永久删除任务（硬删除，不可恢复、不进回收站）。用 id 或唯一的 title 定位。",
                ["inputSchema"] = matchSchema
            },
            new JsonObject
            {
                ["name"] = "get_overview",
                ["description"] = "今日任务概览：总数、已完成、未完成、由前日顺延的数量。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }
            },
            new JsonObject
            {
                ["name"] = "create_note",
                ["description"] = "在今天的随记里记一条想法。随记只能写今天，不能指定过去或未来日期。可引用已存在的随记。",
                ["inputSchema"] = createNoteSchema
            },
            new JsonObject
            {
                ["name"] = "list_notes",
                ["description"] = "查看某一天（默认今天）的随记，返回 id、日期、时间、正文和引用。all=true 可查全部历史。",
                ["inputSchema"] = listNotesSchema
            },
            new JsonObject
            {
                ["name"] = "search_notes",
                ["description"] = "在所有随记里搜索关键词（大小写不敏感），可限定日期范围；结果按日期倒序。",
                ["inputSchema"] = searchNotesSchema
            },
            new JsonObject
            {
                ["name"] = "update_note",
                ["description"] = "修改今天的随记正文和/或引用。历史随记只读；至少提供 text、references 或 clear_references 之一。",
                ["inputSchema"] = updateNoteSchema
            },
            new JsonObject
            {
                ["name"] = "delete_note",
                ["description"] = "永久删除今天的随记（不可恢复）。历史随记只读，不能通过本工具删除。",
                ["inputSchema"] = deleteNoteSchema
            }
        };
    }

    private JsonObject CallTool(JsonObject? p)
    {
        var name = Str(p, "name") ?? string.Empty;
        var args = p?["arguments"] as JsonObject ?? new JsonObject();

        try
        {
            switch (name)
            {
                case "create_task":
                {
                    var item = _tasks.CreateTask(Str(args, "title"), Str(args, "date"),
                        ParsePriority(args), GetTags(args));
                    return ToolResult(
                        $"已创建任务「{item.Title}」，日期 {item.DateCurrent}，优先级 {PriorityName(item.Priority)}。",
                        ToDto(item), false);
                }
                case "list_tasks":
                {
                    var date = Str(args, "date");
                    var tasks = _tasks.ListTasks(date, Bool(args, "include_completed", true),
                        Bool(args, "all", false));
                    return ToolResult(
                        $"共 {tasks.Count} 条任务" + (string.IsNullOrWhiteSpace(date) ? "（今天）" : $"（{date}）") + "。",
                        tasks.Select(ToDto).ToList(), false);
                }
                case "complete_task":
                {
                    var item = _tasks.CompleteTask(Str(args, "id"), Str(args, "title"),
                        Bool(args, "undo", false));
                    return ToolResult(
                        item.DateCompleted == null
                            ? $"已取消完成「{item.Title}」。"
                            : $"已完成「{item.Title}」（{item.DateCompleted}）。",
                        ToDto(item), false);
                }
                case "delete_task":
                {
                    var item = _tasks.DeleteTask(Str(args, "id"), Str(args, "title"));
                    return ToolResult($"已永久删除「{item.Title}」，不可恢复。", ToDto(item), false);
                }
                case "get_overview":
                    return ToolResult("今日概览：", _tasks.GetOverview(), false);
                case "create_note":
                {
                    var item = _notes.CreateNote(Str(args, "text"), ParseRefs(args));
                    return ToolResult($"已记录随记（{item.Date} {item.CreatedAt}）。",
                        _notes.Describe(item), false);
                }
                case "list_notes":
                {
                    var date = Str(args, "date");
                    var all = Bool(args, "all", false);
                    var notes = _notes.ListNotes(date, all, Int(args, "limit", 100));
                    var scope = all ? "（全部日期）" : string.IsNullOrWhiteSpace(date) ? "（今天）" : $"（{date}）";
                    return ToolResult($"共 {notes.Count} 条随记{scope}。", _notes.Describe(notes), false);
                }
                case "search_notes":
                {
                    var query = Str(args, "query");
                    var notes = _notes.SearchNotes(query, Str(args, "from"), Str(args, "to"),
                        Int(args, "limit", 100));
                    return ToolResult($"「{query}」匹配 {notes.Count} 条随记。",
                        _notes.Describe(notes), false);
                }
                case "update_note":
                {
                    var item = _notes.UpdateNote(Str(args, "id"), Str(args, "text"),
                        ParseRefs(args), Bool(args, "clear_references", false));
                    return ToolResult($"已更新 {item.Date} {item.CreatedAt} 的随记。",
                        _notes.Describe(item), false);
                }
                case "delete_note":
                {
                    var item = _notes.DeleteNote(Str(args, "id"));
                    return ToolResult($"已永久删除 {item.Date} {item.CreatedAt} 的随记，不可恢复。",
                        _notes.Describe(item), false);
                }
                default:
                    return ToolResult($"未知工具：{name}", null, true);
            }
        }
        catch (Exception ex)
        {
            return ToolResult("工具调用失败：" + ex.Message, null, true);
        }
    }

    private static JsonObject ToolResult(string message, object? payload, bool isError)
    {
        var text = message;
        if (payload != null) text += "\n" + JsonSerializer.Serialize(payload, Pretty);
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = text }
            },
            ["isError"] = isError
        };
    }

    private static object ToDto(TaskItem t) => new
    {
        id = t.Id.ToString(),
        title = t.Title,
        date = t.DateCurrent,
        date_origin = t.DateOrigin,
        completed = t.DateCompleted,
        priority = PriorityName(t.Priority),
        tags = t.Tags,
        postpone_days = t.PostponeDays
    };

    private static string PriorityName(int p) => p switch
    {
        2 => "high", 0 => "low", _ => "normal"
    };

    private static int ParsePriority(JsonObject args)
    {
        var raw = Str(args, "priority")?.Trim();
        if (string.IsNullOrEmpty(raw)) return 1;
        return raw.ToLowerInvariant() switch
        {
            "high" or "2" or "高" or "高优先级" => 2,
            "low" or "0" or "低" or "低优先级" => 0,
            "normal" or "1" or "中" or "中优先级" or "medium" => 1,
            _ => throw new ArgumentException("priority 只能是 low / normal / high（或 高/中/低、0/1/2）")
        };
    }

    private static IEnumerable<string>? GetTags(JsonObject args)
    {
        if (!args.TryGetPropertyValue("tags", out var node) || node == null) return null;
        if (node is JsonArray arr)
            return arr.Select(x =>
            {
                // JsonNode.ToJsonString() escapes non-ASCII as \uXXXX - never use it
                // for string values or Chinese tags would be stored literally escaped.
                if (x is JsonValue v && v.TryGetValue<string>(out var s)) return s;
                return x?.ToJsonString() ?? string.Empty;
            }).ToList();
        return Str(args, "tags")?.Split(new[] { ',', '，', ';', '；' },
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool Bool(JsonObject args, string name, bool fallback)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node == null) return fallback;
        if (node is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        var s = node.ToJsonString().Trim('"');
        if (bool.TryParse(s, out var parsed)) return parsed;
        return s switch { "1" => true, "0" => false, _ => fallback };
    }

    private static int Int(JsonObject args, string name, int fallback)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node == null) return fallback;
        if (node is JsonValue v && v.TryGetValue<int>(out var i)) return i;
        return int.TryParse(node.ToJsonString().Trim('"'), out var parsed) ? parsed : fallback;
    }

    /// <summary>Missing means "leave references alone"; [] means "clear" on update.</summary>
    private static List<NoteRefInput>? ParseRefs(JsonObject args)
    {
        if (!args.TryGetPropertyValue("references", out var node) || node == null) return null;
        if (node is not JsonArray arr)
            throw new ArgumentException("references 必须是随记 id 数组");

        var result = new List<NoteRefInput>();
        foreach (var item in arr)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out var id))
                result.Add(new NoteRefInput(id));
            else if (item is JsonObject o)
                result.Add(new NoteRefInput(Str(o, "id") ?? string.Empty,
                    Str(o, "date"), Str(o, "text")));
            else throw new ArgumentException("references 元素必须是 id 字符串或 {id,...} 对象");
        }
        return result;
    }

    private static string? Str(JsonObject? obj, string name)
    {
        if (obj == null || !obj.TryGetPropertyValue(name, out var node) || node == null) return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return node.ToJsonString();
    }

    private static JsonObject Ok(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    };

    private static string Serialize(JsonNode node) => node.ToJsonString(Compact);
}
