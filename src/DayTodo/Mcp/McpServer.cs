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

    public McpServer(McpTaskService tasks) => _tasks = tasks;

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
                ["version"] = "1.0.0",
                ["description"] = "DayTodo 日清单：让 AI 客户端创建、查询、完成和删除每日任务"
            },
            ["instructions"] =
                "用 create_task 创建任务（默认今天，可指定今天或未来的 yyyy-MM-dd）；" +
                "用 list_tasks 查看某天的任务并获取 id；" +
                "complete_task / delete_task 建议传 id。历史日期只读，delete_task 是硬删除且不可恢复。"
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
