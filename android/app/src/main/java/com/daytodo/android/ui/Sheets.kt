@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)

package com.daytodo.android.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.filled.Undo
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Link
import androidx.compose.material.icons.filled.Today
import androidx.compose.material3.BottomSheetDefaults
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daytodo.android.model.Dates
import com.daytodo.android.model.TaskItem
import java.time.YearMonth
import kotlinx.coroutines.delay

private val SheetShape = RoundedCornerShape(topStart = 22.dp, topEnd = 22.dp)

@Composable
private fun sheetColors() = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = TodoColors.Accent,
    unfocusedBorderColor = TodoColors.LineStrong,
    focusedContainerColor = TodoColors.Surface,
    unfocusedContainerColor = TodoColors.Surface,
    focusedTextColor = TodoColors.Text1,
    unfocusedTextColor = TodoColors.Text1,
    cursorColor = TodoColors.Accent
)

/* ==================== 新建 / 撤回后重发 ==================== */

@Composable
fun AddTaskSheet(
    dateLabel: String,
    isRecall: Boolean,
    initialTitle: String,
    initialTags: List<String>,
    initialPriority: Int,
    onDismiss: () -> Unit,
    onSave: (title: String, tags: List<String>, priority: Int) -> Unit
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor = TodoColors.Surface,
        shape = SheetShape,
        dragHandle = { BottomSheetDefaults.DragHandle(color = TodoColors.LineStrong) }
    ) {
        var title by rememberSaveable { mutableStateOf(initialTitle) }
        var priority by rememberSaveable { mutableIntStateOf(initialPriority) }
        var tags by remember { mutableStateOf(initialTags.toList()) }
        var tagInput by remember { mutableStateOf("") }
        val focusRequester = remember { FocusRequester() }

        LaunchedEffect(Unit) {
            delay(280)
            focusRequester.requestFocus()
        }

        Column(
            Modifier
                .fillMaxWidth()
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(start = 20.dp, end = 20.dp, bottom = 18.dp)
        ) {
            Text(
                text = if (isRecall) "改完重新发送" else "新建任务",
                style = MaterialTheme.typography.titleMedium,
                color = TodoColors.Text1
            )
            Spacer(Modifier.height(3.dp))
            Text(
                text = if (isRecall) "$dateLabel · 内容已从撤回的任务回填" else dateLabel,
                style = MaterialTheme.typography.bodySmall,
                color = TodoColors.Text3
            )
            Spacer(Modifier.height(12.dp))

            OutlinedTextField(
                value = title,
                onValueChange = { title = it },
                modifier = Modifier.fillMaxWidth().focusRequester(focusRequester),
                placeholder = { Text("要做什么？", fontSize = 15.sp, color = TodoColors.Text3) },
                textStyle = MaterialTheme.typography.bodyLarge.copy(color = TodoColors.Text1),
                shape = RoundedCornerShape(12.dp),
                minLines = 2,
                maxLines = 4,
                colors = sheetColors()
            )

            FieldLabel("标签")
            if (tags.isNotEmpty()) {
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    tags.forEach { tag ->
                        RemovableTag(tag) { tags = tags - tag }
                    }
                }
                Spacer(Modifier.height(6.dp))
            }
            TagInput(
                value = tagInput,
                onValueChange = { tagInput = it },
                onSubmit = {
                    val t = tagInput.trim()
                    if (t.isNotEmpty() && tags.size < 4) {
                        tags = tags + t
                        tagInput = ""
                    }
                }
            )

            FieldLabel("优先级")
            PrioritySegmented(current = priority, onSelect = { priority = it })

            Spacer(Modifier.height(14.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedButton(
                    onClick = onDismiss,
                    modifier = Modifier.weight(1f).height(44.dp),
                    shape = RoundedCornerShape(12.dp),
                    border = BorderStroke(1.dp, TodoColors.LineStrong)
                ) {
                    Text("取消", fontSize = 14.5.sp, color = TodoColors.Text2)
                }
                Button(
                    onClick = { if (title.isNotBlank()) onSave(title, tags, priority) },
                    modifier = Modifier.weight(1.6f).height(44.dp),
                    shape = RoundedCornerShape(12.dp),
                    enabled = title.isNotBlank(),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = TodoColors.Accent,
                        disabledContainerColor = Color(0xFFC9D6E2),
                        contentColor = Color.White
                    )
                ) {
                    Text(if (isRecall) "重新发送" else "保存任务", fontSize = 14.5.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

@Composable
private fun FieldLabel(text: String) {
    Text(
        text = text,
        fontSize = 12.sp,
        fontWeight = FontWeight.SemiBold,
        color = TodoColors.Text3,
        modifier = Modifier.padding(top = 13.dp, bottom = 6.dp)
    )
}

/**
 * 标签输入：Material 的 OutlinedTextField 最低也有 56dp，放在这里显得比标题框还厚。
 * 用 BasicTextField 自绘 44dp 胶囊，保留回车提交与 IME 动作。
 */
@Composable
private fun TagInput(
    value: String,
    onValueChange: (String) -> Unit,
    onSubmit: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(44.dp)
            .clip(RoundedCornerShape(12.dp))
            .border(1.dp, TodoColors.LineStrong, RoundedCornerShape(12.dp))
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            modifier = Modifier.weight(1f),
            singleLine = true,
            textStyle = TextStyle(fontSize = 14.sp, color = TodoColors.Text1),
            cursorBrush = SolidColor(TodoColors.Accent),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { onSubmit() }),
            decorationBox = { inner ->
                Box(contentAlignment = Alignment.CenterStart) {
                    if (value.isEmpty()) {
                        Text("输入标签后回车，最多 4 个", fontSize = 13.sp, color = TodoColors.Text3)
                    }
                    inner()
                }
            }
        )
        if (value.isNotBlank()) {
            TextButton(
                onClick = onSubmit,
                contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp),
                modifier = Modifier.height(32.dp)
            ) {
                Text("添加", fontSize = 13.sp, color = TodoColors.AccentDark)
            }
        }
    }
}

@Composable
private fun RemovableTag(text: String, onRemove: () -> Unit) {
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(8.dp))
            .background(TodoColors.Chip)
            .padding(start = 10.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text, fontSize = 13.sp, color = TodoColors.Text2)
        Spacer(Modifier.width(8.dp))
        Box(
            Modifier
                .size(20.dp)
                .clip(CircleShape)
                .clickable(onClick = onRemove),
            contentAlignment = Alignment.Center
        ) {
            Text("×", fontSize = 15.sp, color = TodoColors.Text3)
        }
    }
}

@Composable
private fun PrioritySegmented(current: Int, onSelect: (Int) -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(38.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(TodoColors.Chip)
            .padding(3.dp)
    ) {
        listOf(2 to "高", 1 to "中", 0 to "低").forEach { (value, label) ->
            val selected = current == value
            Row(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .clip(RoundedCornerShape(8.dp))
                    .background(if (selected) TodoColors.Surface else Color.Transparent)
                    .clickable { onSelect(value) },
                horizontalArrangement = Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically
            ) {
                val dot = when (value) {
                    2 -> TodoColors.High
                    1 -> TodoColors.Mid
                    else -> TodoColors.Low
                }
                Box(Modifier.size(6.dp).clip(CircleShape).background(dot))
                Spacer(Modifier.width(5.dp))
                Text(
                    text = label,
                    fontSize = 13.5.sp,
                    fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
                    color = if (selected) TodoColors.Text1 else TodoColors.Text3
                )
            }
        }
    }
}

/* ==================== 任务操作 ==================== */

@Composable
fun TaskActionsSheet(
    task: TaskItem,
    canPullToToday: Boolean,
    canEdit: Boolean,
    canReschedule: Boolean,
    onPullToToday: () -> Unit,
    onReschedule: () -> Unit,
    onRecall: () -> Unit,
    onDelete: () -> Unit,
    onDismiss: () -> Unit
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor = TodoColors.Surface,
        shape = SheetShape,
        dragHandle = { BottomSheetDefaults.DragHandle(color = TodoColors.LineStrong) }
    ) {
        Column(Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, bottom = 24.dp)) {
            Text("任务操作", style = MaterialTheme.typography.titleMedium, color = TodoColors.Text1)
            Spacer(Modifier.height(3.dp))
            Text(
                text = task.title,
                style = MaterialTheme.typography.bodySmall,
                color = TodoColors.Text3,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
            Spacer(Modifier.height(16.dp))

            Surface(
                shape = RoundedCornerShape(12.dp),
                color = TodoColors.Surface,
                border = BorderStroke(1.dp, TodoColors.Line)
            ) {
                Column {
                    if (canPullToToday) {
                        ActionItem(Icons.Filled.Today, "拉回今天", "顺延项回到今天", TodoColors.AccentDark) {
                            onPullToToday()
                        }
                    }
                    if (canReschedule) {
                        ActionItem(Icons.Filled.CalendarMonth, "改期", "选一个新的日期", TodoColors.Mid) {
                            onReschedule()
                        }
                    }
                    if (canEdit) {
                        ActionItem(Icons.AutoMirrored.Filled.Undo, "撤回", "内容回填到编辑框", TodoColors.AccentDark) {
                            onRecall()
                        }
                        ActionItem(Icons.Filled.Delete, "删除", "不可恢复", TodoColors.High, danger = true) {
                            onDelete()
                        }
                    }
                    if (!canEdit && !canReschedule) {
                        Text(
                            text = "历史记录为只读，无法修改或删除。",
                            fontSize = 13.sp,
                            color = TodoColors.Text3,
                            modifier = Modifier.padding(16.dp)
                        )
                    }
                }
            }

            Spacer(Modifier.height(16.dp))
            OutlinedButton(
                onClick = onDismiss,
                modifier = Modifier.fillMaxWidth().height(48.dp),
                shape = RoundedCornerShape(12.dp),
                border = BorderStroke(1.dp, TodoColors.LineStrong)
            ) {
                Text("取消", fontSize = 15.sp, color = TodoColors.Text2)
            }
        }
    }
}

/* ==================== 随记操作 ==================== */

@Composable
fun NoteActionsSheet(
    preview: String,
    canEdit: Boolean,
    onQuote: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
    onDismiss: () -> Unit
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor = TodoColors.Surface,
        shape = SheetShape,
        dragHandle = { BottomSheetDefaults.DragHandle(color = TodoColors.LineStrong) }
    ) {
        Column(Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, bottom = 24.dp)) {
            Text("随记操作", style = MaterialTheme.typography.titleMedium, color = TodoColors.Text1)
            Spacer(Modifier.height(3.dp))
            Text(
                text = preview,
                style = MaterialTheme.typography.bodySmall,
                color = TodoColors.Text3,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Spacer(Modifier.height(16.dp))

            Surface(
                shape = RoundedCornerShape(12.dp),
                color = TodoColors.Surface,
                border = BorderStroke(1.dp, TodoColors.Line)
            ) {
                Column {
                    ActionItem(Icons.Filled.Link, "引用", "跳回今天并加入草稿", TodoColors.AccentDark) {
                        onQuote()
                    }
                    if (canEdit) {
                        ActionItem(Icons.Filled.Edit, "编辑", "在输入框里修改", TodoColors.AccentDark) {
                            onEdit()
                        }
                        ActionItem(Icons.Filled.Delete, "删除", "不可恢复", TodoColors.High, danger = true) {
                            onDelete()
                        }
                    }
                }
            }

            Spacer(Modifier.height(16.dp))
            OutlinedButton(
                onClick = onDismiss,
                modifier = Modifier.fillMaxWidth().height(48.dp),
                shape = RoundedCornerShape(12.dp),
                border = BorderStroke(1.dp, TodoColors.LineStrong)
            ) {
                Text("取消", fontSize = 15.sp, color = TodoColors.Text2)
            }
        }
    }
}

@Composable
private fun ActionItem(
    icon: ImageVector,
    label: String,
    hint: String,
    tint: Color,
    danger: Boolean = false,
    onClick: () -> Unit
) {
    Column {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp)
                .clickable(onClick = onClick)
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                Modifier
                    .size(32.dp)
                    .clip(RoundedCornerShape(10.dp))
                    .background(if (danger) TodoColors.HighBg else TodoColors.AccentLight),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = tint,
                    modifier = Modifier.size(18.dp)
                )
            }
            Spacer(Modifier.width(12.dp))
            Text(
                text = label,
                fontSize = 15.sp,
                color = if (danger) TodoColors.High else TodoColors.Text1,
                modifier = Modifier.weight(1f)
            )
            Text(text = hint, fontSize = 12.sp, color = TodoColors.Text3)
        }
        HorizontalDivider(color = TodoColors.Line)
    }
}

/* ==================== 随记编辑 / 新建 ==================== */

@Composable
fun NoteComposerSheet(
    initialText: String,
    isEditing: Boolean,
    quoteLabel: String?,
    onClearQuote: () -> Unit,
    onDismiss: () -> Unit,
    onSave: (String) -> Unit
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor = TodoColors.Surface,
        shape = SheetShape,
        dragHandle = { BottomSheetDefaults.DragHandle(color = TodoColors.LineStrong) }
    ) {
        var text by rememberSaveable { mutableStateOf(initialText) }
        val focusRequester = remember { FocusRequester() }

        LaunchedEffect(Unit) {
            delay(280)
            focusRequester.requestFocus()
        }

        Column(
            Modifier
                .fillMaxWidth()
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(start = 20.dp, end = 20.dp, bottom = 18.dp)
        ) {
            Text(
                text = if (isEditing) "编辑随记" else "新建随记",
                style = MaterialTheme.typography.titleMedium,
                color = TodoColors.Text1
            )
            Spacer(Modifier.height(3.dp))
            Text(
                text = "可写多行，写好点保存",
                style = MaterialTheme.typography.bodySmall,
                color = TodoColors.Text3
            )

            if (quoteLabel != null) {
                Spacer(Modifier.height(12.dp))
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(10.dp))
                        .background(TodoColors.AccentLight)
                        .padding(start = 12.dp, end = 6.dp, top = 8.dp, bottom = 8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = quoteLabel,
                        fontSize = 13.sp,
                        color = TodoColors.AccentDark,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f)
                    )
                    TextButton(onClick = onClearQuote, contentPadding = PaddingValuesZero) {
                        Text("移除", fontSize = 13.sp, color = TodoColors.AccentDark)
                    }
                }
            }

            Spacer(Modifier.height(14.dp))
            OutlinedTextField(
                value = text,
                onValueChange = { text = it },
                modifier = Modifier.fillMaxWidth().height(180.dp).focusRequester(focusRequester),
                placeholder = { Text("随手记下现在的想法…", fontSize = 15.sp, color = TodoColors.Text3) },
                textStyle = MaterialTheme.typography.bodyLarge.copy(color = TodoColors.Text1),
                shape = RoundedCornerShape(12.dp),
                colors = sheetColors()
            )

            Spacer(Modifier.height(14.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedButton(
                    onClick = onDismiss,
                    modifier = Modifier.weight(1f).height(44.dp),
                    shape = RoundedCornerShape(12.dp),
                    border = BorderStroke(1.dp, TodoColors.LineStrong)
                ) {
                    Text("取消", fontSize = 14.5.sp, color = TodoColors.Text2)
                }
                Button(
                    onClick = { if (text.isNotBlank()) onSave(text) },
                    modifier = Modifier.weight(1.6f).height(44.dp),
                    shape = RoundedCornerShape(12.dp),
                    enabled = text.isNotBlank(),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = TodoColors.Accent,
                        disabledContainerColor = Color(0xFFC9D6E2),
                        contentColor = Color.White
                    )
                ) {
                    Text(if (isEditing) "保存修改" else "保存", fontSize = 14.5.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

private val PaddingValuesZero = androidx.compose.foundation.layout.PaddingValues(horizontal = 8.dp, vertical = 0.dp)

/* ==================== 日期选择 / 改期 ==================== */

@Composable
fun DateSheet(
    selected: String,
    title: String = "选择日期",
    onPick: (String) -> Unit,
    onDismiss: () -> Unit
) {
    var month by remember { mutableStateOf(YearMonth.from(Dates.parse(selected))) }
    val today = Dates.today()

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor = TodoColors.Surface,
        shape = SheetShape,
        dragHandle = { BottomSheetDefaults.DragHandle(color = TodoColors.LineStrong) }
    ) {
        Column(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 20.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(title, style = MaterialTheme.typography.titleMedium, color = TodoColors.Text1)
                Spacer(Modifier.weight(1f))
                TextButton(onClick = onDismiss, contentPadding = PaddingValuesZero) {
                    Text("取消", fontSize = 14.sp, color = TodoColors.Text2)
                }
            }

            Row(
                modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Box(
                    Modifier
                        .size(44.dp)
                        .clip(CircleShape)
                        .clickable { month = month.minusMonths(1) },
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        Icons.AutoMirrored.Filled.KeyboardArrowLeft,
                        contentDescription = "上个月",
                        tint = TodoColors.Text2,
                        modifier = Modifier.size(22.dp)
                    )
                }
                Text(
                    text = "${month.year} 年 ${month.monthValue} 月",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = TodoColors.Text1,
                    textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                    modifier = Modifier.weight(1f)
                )
                Box(
                    Modifier
                        .size(44.dp)
                        .clip(CircleShape)
                        .clickable { month = month.plusMonths(1) },
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        Icons.AutoMirrored.Filled.KeyboardArrowRight,
                        contentDescription = "下个月",
                        tint = TodoColors.Text2,
                        modifier = Modifier.size(22.dp)
                    )
                }
            }

            Row(Modifier.fillMaxWidth().padding(top = 2.dp)) {
                WEEK_HEADERS.forEach { w ->
                    Text(
                        text = w,
                        fontSize = 12.sp,
                        color = TodoColors.Text3,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                        modifier = Modifier.weight(1f).padding(vertical = 6.dp)
                    )
                }
            }

            CalendarGrid(
                month = month,
                selected = selected,
                today = today,
                onPick = onPick
            )

            Spacer(Modifier.height(12.dp))
            OutlinedButton(
                onClick = { onPick(today) },
                modifier = Modifier.fillMaxWidth().height(48.dp),
                shape = RoundedCornerShape(12.dp),
                border = BorderStroke(1.dp, TodoColors.LineStrong)
            ) {
                Text("回到今天", fontSize = 15.sp, color = TodoColors.AccentDark)
            }
        }
    }
}

private val WEEK_HEADERS = listOf("一", "二", "三", "四", "五", "六", "日")

@Composable
private fun CalendarGrid(
    month: YearMonth,
    selected: String,
    today: String,
    onPick: (String) -> Unit
) {
    val leading = (month.atDay(1).dayOfWeek.value + 6) % 7
    val days = month.lengthOfMonth()
    val rows = ((leading + days) + 6) / 7

    Column {
        repeat(rows) { r ->
            Row(Modifier.fillMaxWidth()) {
                repeat(7) { c ->
                    val index = r * 7 + c - leading
                    if (index in 0 until days) {
                        val date = month.atDay(index + 1)
                        val iso = Dates.format(date)
                        val isSelected = iso == selected
                        val isToday = iso == today
                        Box(
                            modifier = Modifier.weight(1f).height(44.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            Box(
                                modifier = Modifier
                                    .size(38.dp)
                                    .clip(CircleShape)
                                    .background(if (isSelected) TodoColors.Accent else Color.Transparent)
                                    .clickable { onPick(iso) },
                                contentAlignment = Alignment.Center
                            ) {
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    Text(
                                        text = date.dayOfMonth.toString(),
                                        fontSize = 14.sp,
                                        fontWeight = if (isSelected || isToday) FontWeight.SemiBold else FontWeight.Normal,
                                        color = when {
                                            isSelected -> Color.White
                                            isToday -> TodoColors.AccentDark
                                            else -> TodoColors.Text1
                                        }
                                    )
                                    Spacer(Modifier.height(1.dp))
                                    Box(
                                        Modifier
                                            .size(3.dp)
                                            .clip(CircleShape)
                                            .background(
                                                when {
                                                    !isToday -> Color.Transparent
                                                    isSelected -> Color.White
                                                    else -> TodoColors.Accent
                                                }
                                            )
                                    )
                                }
                            }
                        }
                    } else {
                        Spacer(Modifier.weight(1f))
                    }
                }
            }
        }
    }
}
