package com.daytodo.android.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.daytodo.android.model.Dates
import com.daytodo.android.model.NoteItem
import com.daytodo.android.model.NoteReference
import com.daytodo.android.model.TaskItem

private val WEEK_SHORT = listOf("一", "二", "三", "四", "五", "六", "日")

internal fun dateTitle(iso: String): String {
    val d = Dates.parse(iso)
    return "${d.monthValue}月${d.dayOfMonth}日"
}

internal fun weekShort(iso: String): String = WEEK_SHORT[Dates.parse(iso).dayOfWeek.value - 1]

internal fun weekFull(iso: String): String = "周" + weekShort(iso)

internal fun dayOfMonth(iso: String): String = Dates.parse(iso).dayOfMonth.toString()

internal fun timeLabel(createdAt: Long): String =
    java.time.Instant.ofEpochMilli(createdAt)
        .atZone(java.time.ZoneId.systemDefault())
        .toLocalTime()
        .format(java.time.format.DateTimeFormatter.ofPattern("HH:mm"))

/** 带日期的创建时间：全文卡片里要能看出是哪天记的。 */
internal fun createdLabel(createdAt: Long): String =
    java.time.Instant.ofEpochMilli(createdAt)
        .atZone(java.time.ZoneId.systemDefault())
        .format(java.time.format.DateTimeFormatter.ofPattern("M月d日 HH:mm"))

@Composable
fun MainScreen(
    vm: MainViewModel,
    snackbarHost: @Composable () -> Unit,
    onOpenCalendar: () -> Unit,
    onAddTask: () -> Unit,
    onAddNote: () -> Unit,
    onTaskActions: (TaskItem) -> Unit,
    onNoteActions: (NoteItem) -> Unit,
    onOpenReference: (NoteReference) -> Unit,
    onSettings: () -> Unit,
    onClearCompleted: () -> Unit
) {
    val today = Dates.today()
    val selected = vm.selectedDate
    val isToday = selected == today
    val isPast = selected < today

    // 全文对话框指向一条具体任务，切日期后它就不再成立，直接关掉。
    var detailRow by remember { mutableStateOf<TaskRow?>(null) }
    LaunchedEffect(selected) { detailRow = null }

    Scaffold(
        containerColor = TodoColors.Bg,
        snackbarHost = snackbarHost,
        bottomBar = {
            BottomNav(
                current = vm.tab,
                onTasks = { vm.updateTab(AppTab.Tasks) },
                onNotes = { vm.updateTab(AppTab.Notes) },
                onSettings = onSettings
            )
        },
        floatingActionButton = {
            AnimatedVisibility(
                // 任务：今天和未来可加；随记：只能记当天。
                visible = !isPast && (vm.tab == AppTab.Tasks || isToday),
                enter = fadeIn() + expandVertically(),
                exit = fadeOut() + shrinkVertically()
            ) {
                FloatingActionButton(
                    onClick = { if (vm.tab == AppTab.Notes) onAddNote() else onAddTask() },
                    containerColor = TodoColors.Accent,
                    contentColor = Color.White,
                    shape = RoundedCornerShape(20.dp),
                    modifier = Modifier.size(56.dp)
                ) {
                    Icon(
                        imageVector = Icons.Filled.Add,
                        contentDescription = if (vm.tab == AppTab.Notes) "记一笔" else "新建任务",
                        modifier = Modifier.size(26.dp)
                    )
                }
            }
        }
    ) { padding ->
        Column(modifier = Modifier.fillMaxSize().padding(padding)) {
            AppHeader(
                vm = vm,
                isToday = isToday,
                isPast = isPast,
                onOpenCalendar = onOpenCalendar,
                onClearCompleted = onClearCompleted
            )
            if (isPast) ReadonlyBanner()

            if (vm.isLoading) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(color = TodoColors.Accent)
                }
            } else {
                when (vm.tab) {
                    AppTab.Tasks -> TaskSection(
                        vm = vm,
                        isPast = isPast,
                        onActions = onTaskActions,
                        onOpenDetail = { detailRow = it }
                    )
                    AppTab.Notes -> NoteSection(
                        vm = vm,
                        isToday = isToday,
                        isPast = isPast,
                        onActions = onNoteActions,
                        onOpenReference = onOpenReference
                    )
                }
            }
        }
    }

    detailRow?.let { row ->
        TaskDetailDialog(row = row, onDismiss = { detailRow = null })
    }
}

@Composable
private fun AppHeader(
    vm: MainViewModel,
    isToday: Boolean,
    isPast: Boolean,
    onOpenCalendar: () -> Unit,
    onClearCompleted: () -> Unit
) {
    Surface(color = TodoColors.Surface, border = BorderStroke(0.5.dp, TodoColors.Line)) {
        Column {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(start = 16.dp, end = 6.dp, top = 6.dp),
                verticalAlignment = Alignment.Top
            ) {
                Column(modifier = Modifier.weight(1f).padding(bottom = 2.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = dateTitle(vm.selectedDate),
                            style = MaterialTheme.typography.displaySmall,
                            color = TodoColors.Text1
                        )
                        Spacer(Modifier.width(8.dp))
                        RelationBadge(
                            text = if (isToday) "今天" else Dates.relation(vm.selectedDate),
                            highlight = isToday
                        )
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(weekFull(vm.selectedDate), style = MaterialTheme.typography.bodySmall, color = TodoColors.Text3)
                        if (!isToday) {
                            Spacer(Modifier.width(4.dp))
                            TextButton(
                                onClick = { vm.goToday() },
                                contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp),
                                modifier = Modifier.height(32.dp)
                            ) {
                                Text("回到今天", fontSize = 13.sp, color = TodoColors.AccentDark)
                            }
                        }
                    }
                }
                IconButton(onClick = onOpenCalendar, modifier = Modifier.size(44.dp)) {
                    Icon(
                        Icons.Filled.CalendarMonth,
                        contentDescription = "选择日期",
                        tint = TodoColors.Text2,
                        modifier = Modifier.size(22.dp)
                    )
                }
            }

            DateStrip(
                selected = vm.selectedDate,
                today = Dates.today(),
                hasContent = { vm.hasTasksOn(it) || vm.hasNotesOn(it) },
                onSelect = { vm.selectDate(it) }
            )

            SummaryRow(vm = vm, isPast = isPast, onClearCompleted = onClearCompleted)
        }
    }
}

@Composable
private fun RelationBadge(text: String, highlight: Boolean) {
    Surface(
        color = if (highlight) TodoColors.AccentLight else TodoColors.Chip,
        shape = CircleShape
    ) {
        Text(
            text = text,
            fontSize = 12.sp,
            fontWeight = FontWeight.Medium,
            color = if (highlight) TodoColors.AccentDark else TodoColors.Text2,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 3.dp)
        )
    }
}

/**
 * 横向日期条，覆盖「今天 - 6 天」到「今天 + 7 天」，选中项自动滚到视野中部。
 * 这是替代原来 24dp 左右箭头的方案：一步跳到位，而不是一天一天翻。
 */
@Composable
private fun DateStrip(
    selected: String,
    today: String,
    hasContent: (String) -> Boolean,
    onSelect: (String) -> Unit
) {
    val dates = remember(today) { (-6..7).map { Dates.shift(today, it.toLong()) } }
    val selectedIndex = dates.indexOf(selected)
    val listState = rememberLazyListState()

    LaunchedEffect(selectedIndex, dates) {
        if (selectedIndex >= 0) {
            val target = (selectedIndex - 2).coerceIn(0, (dates.size - 1).coerceAtLeast(0))
            listState.animateScrollToItem(target)
        }
    }

    LazyRow(
        state = listState,
        modifier = Modifier.fillMaxWidth().padding(top = 10.dp, bottom = 8.dp),
        contentPadding = PaddingValues(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        items(dates, key = { it }) { date ->
            DateChip(
                date = date,
                isSelected = date == selected,
                isToday = date == today,
                hasContent = hasContent(date),
                onClick = { onSelect(date) }
            )
        }
    }
}

@Composable
private fun DateChip(
    date: String,
    isSelected: Boolean,
    isToday: Boolean,
    hasContent: Boolean,
    onClick: () -> Unit
) {
    Column(
        modifier = Modifier
            .width(46.dp)
            .height(60.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(if (isSelected) TodoColors.Accent else TodoColors.Panel)
            .border(
                width = 1.dp,
                color = if (isSelected) TodoColors.Accent else Color.Transparent,
                shape = RoundedCornerShape(14.dp)
            )
            .clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Text(
            text = weekShort(date),
            fontSize = 11.sp,
            fontWeight = if (isToday) FontWeight.SemiBold else FontWeight.Normal,
            color = when {
                isSelected -> Color.White
                isToday -> TodoColors.AccentDark
                else -> TodoColors.Text3
            }
        )
        Spacer(Modifier.height(2.dp))
        Text(
            text = dayOfMonth(date),
            fontSize = 17.sp,
            fontWeight = FontWeight.SemiBold,
            color = if (isSelected) Color.White else TodoColors.Text1
        )
        Spacer(Modifier.height(3.dp))
        Box(
            Modifier
                .size(4.dp)
                .clip(CircleShape)
                .background(
                    when {
                        !hasContent -> Color.Transparent
                        isSelected -> Color.White
                        else -> TodoColors.Accent
                    }
                )
        )
    }
}

@Composable
private fun SummaryRow(
    vm: MainViewModel,
    isPast: Boolean,
    onClearCompleted: () -> Unit
) {
    val total = vm.rows.size
    val done = vm.doneRows.size
    val carry = vm.carriedRows.size
    val progress = if (total == 0) 0f else done.toFloat() / total

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 16.dp, end = 8.dp, bottom = 10.dp, top = 2.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        val label = if (vm.tab == AppTab.Tasks) {
            if (total == 0) "今天还没有安排" else "$done / $total 完成 · $carry 项顺延"
        } else {
            "${vm.notes.size} 条随记"
        }
        Text(
            text = label,
            style = MaterialTheme.typography.bodySmall,
            color = TodoColors.Text2,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis
        )
        Spacer(Modifier.width(10.dp))
        if (vm.tab == AppTab.Tasks) {
            Box(
                Modifier
                    .weight(1f)
                    .height(4.dp)
                    .clip(CircleShape)
                    .background(TodoColors.Line)
            ) {
                if (progress > 0f) {
                    Box(
                        Modifier
                            .fillMaxWidth(progress)
                            .fillMaxHeight()
                            .clip(CircleShape)
                            .background(TodoColors.Accent)
                    )
                }
            }
        } else {
            Spacer(Modifier.weight(1f))
        }
        if (vm.tab == AppTab.Tasks && done > 0 && !isPast) {
            TextButton(
                onClick = onClearCompleted,
                contentPadding = PaddingValues(horizontal = 8.dp, vertical = 0.dp),
                modifier = Modifier.height(36.dp)
            ) {
                Text("清除已完成", fontSize = 12.sp, color = TodoColors.AccentDark)
            }
        } else {
            Spacer(Modifier.width(8.dp))
        }
    }
}

@Composable
private fun ReadonlyBanner() {
    Surface(
        color = TodoColors.Panel,
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
        shape = RoundedCornerShape(8.dp),
        border = BorderStroke(1.dp, TodoColors.LineStrong)
    ) {
        Row(
            Modifier.padding(horizontal = 12.dp, vertical = 9.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(
                Icons.Filled.Lock,
                contentDescription = null,
                tint = TodoColors.Text3,
                modifier = Modifier.size(15.dp)
            )
            Spacer(Modifier.width(8.dp))
            Text(
                "历史记录 · 只读模式，勾选与编辑已关闭",
                style = MaterialTheme.typography.bodySmall,
                color = TodoColors.Text2
            )
        }
    }
}

/**
 * 底部导航：Material3 的 NavigationBar 固定 80dp 起步，在手机上偏高、22dp 图标显得空。
 * 这里改成 58dp 内容高度 + 24dp 图标，整体矮 22dp，图标反而更大，比例更协调。
 * 手势条区域用 navigationBars inset 单独留出，背景一直铺到屏幕底。
 */
@Composable
private fun BottomNav(
    current: AppTab,
    onTasks: () -> Unit,
    onNotes: () -> Unit,
    onSettings: () -> Unit
) {
    Surface(color = TodoColors.Surface) {
        Column(Modifier.fillMaxWidth().windowInsetsPadding(WindowInsets.navigationBars)) {
            HorizontalDivider(thickness = 0.5.dp, color = TodoColors.Line)
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(58.dp)
                    .padding(horizontal = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                NavTab("任务", Icons.Filled.CheckCircle, current == AppTab.Tasks, onTasks)
                NavTab("随记", Icons.Filled.Description, current == AppTab.Notes, onNotes)
                NavTab("设置", Icons.Filled.Settings, false, onSettings)
            }
        }
    }
}

@Composable
private fun RowScope.NavTab(
    label: String,
    icon: ImageVector,
    selected: Boolean,
    onClick: () -> Unit
) {
    val tint = if (selected) TodoColors.AccentDark else TodoColors.Text3
    Column(
        modifier = Modifier
            .weight(1f)
            .fillMaxHeight()
            .padding(vertical = 4.dp)
            .clip(RoundedCornerShape(12.dp))
            .selectable(selected = selected, role = Role.Tab, onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Icon(
            imageVector = icon,
            contentDescription = null,
            tint = tint,
            modifier = Modifier.size(24.dp)
        )
        Spacer(Modifier.height(2.dp))
        Text(
            text = label,
            fontSize = 11.5.sp,
            fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
            color = tint
        )
    }
}

@Composable
private fun TaskSection(
    vm: MainViewModel,
    isPast: Boolean,
    onActions: (TaskItem) -> Unit,
    onOpenDetail: (TaskRow) -> Unit
) {
    val carried = vm.carriedRows
    val open = vm.openRows
    val done = vm.doneRows

    if (vm.rows.isEmpty()) {
        EmptyState(
            icon = Icons.Filled.CheckCircle,
            title = "这一天还没有任务",
            hint = if (isPast) "换个日期看看" else "点右下角的 + 添加第一条"
        )
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 12.dp, end = 12.dp, top = 8.dp, bottom = 104.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        if (carried.isNotEmpty()) {
            item(key = "header-carried") {
                SectionLabel("顺延自更早", carried.size)
            }
            items(carried, key = { it.item.id }) { row ->
                TaskRowView(
                    row = row,
                    editable = !isPast,
                    showActions = !isPast,
                    onToggle = { vm.toggleTask(row.item) },
                    onActions = { onActions(row.item) },
                    onOpenDetail = { onOpenDetail(row) }
                )
            }
        }

        if (open.isNotEmpty()) {
            item(key = "header-open") {
                SectionLabel("今天", open.size)
            }
            items(open, key = { it.item.id }) { row ->
                TaskRowView(
                    row = row,
                    editable = !isPast,
                    showActions = !isPast,
                    onToggle = { vm.toggleTask(row.item) },
                    onActions = { onActions(row.item) },
                    onOpenDetail = { onOpenDetail(row) }
                )
            }
        }

        if (done.isNotEmpty()) {
            item(key = "header-done") {
                SectionLabel("已完成", done.size)
            }
            items(done, key = { it.item.id }) { row ->
                TaskRowView(
                    row = row,
                    editable = !isPast,
                    showActions = !isPast,
                    onToggle = { vm.toggleTask(row.item) },
                    onActions = { onActions(row.item) },
                    onOpenDetail = { onOpenDetail(row) }
                )
            }
        }
    }
}

@Composable
private fun SectionLabel(text: String, count: Int) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(start = 4.dp, top = 6.dp, bottom = 2.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = text,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = TodoColors.Text3
        )
        Spacer(Modifier.width(8.dp))
        Surface(color = TodoColors.Chip, shape = CircleShape) {
            Text(
                text = count.toString(),
                fontSize = 11.sp,
                color = TodoColors.Text3,
                modifier = Modifier.padding(horizontal = 7.dp, vertical = 1.dp)
            )
        }
    }
}

@Composable
private fun TaskRowView(
    row: TaskRow,
    editable: Boolean,
    showActions: Boolean,
    onToggle: () -> Unit,
    onActions: () -> Unit,
    onOpenDetail: () -> Unit
) {
    val item = row.item
    val shape = RoundedCornerShape(12.dp)

    // 只有真的被两行截断时才可点。用 Compose 自己的布局结果判断，不靠字数猜。
    var overflowed by remember(item.id) { mutableStateOf(false) }

    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = shape,
        color = TodoColors.Surface,
        border = BorderStroke(1.dp, TodoColors.Line)
    ) {
        Row(
            modifier = Modifier
                .clip(shape)
                .drawBehind {
                    if (row.carriedDays > 0L) {
                        drawRect(
                            color = TodoColors.Accent,
                            topLeft = Offset.Zero,
                            size = Size(2.5.dp.toPx(), size.height)
                        )
                    }
                }
                .padding(start = 6.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.Top
        ) {
            // 视觉 24dp，热区 44dp
            Box(
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .toggleable(
                        value = item.done,
                        enabled = editable,
                        role = Role.Checkbox,
                        onValueChange = { onToggle() }
                    ),
                contentAlignment = Alignment.Center
            ) {
                Box(
                    modifier = Modifier
                        .size(24.dp)
                        .clip(CircleShape)
                        .background(if (item.done) TodoColors.Accent else Color.Transparent)
                        .border(
                            width = 1.8.dp,
                            color = if (item.done) TodoColors.Accent else TodoColors.LineStrong,
                            shape = CircleShape
                        ),
                    contentAlignment = Alignment.Center
                ) {
                    if (item.done) {
                        Icon(
                            Icons.Filled.Check,
                            contentDescription = null,
                            tint = Color.White,
                            modifier = Modifier.size(14.dp)
                        )
                    }
                }
            }

            Column(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(8.dp))
                    .clickable(
                        enabled = overflowed,
                        onClickLabel = "查看任务全文",
                        onClick = onOpenDetail
                    )
                    .padding(top = 11.dp, end = 4.dp, bottom = 5.dp)
            ) {
                Text(
                    text = item.title,
                    fontSize = 15.sp,
                    lineHeight = 21.sp,
                    color = if (item.done) TodoColors.Text3 else TodoColors.Text1,
                    textDecoration = if (item.done) TextDecoration.LineThrough else null,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    onTextLayout = { overflowed = it.hasVisualOverflow }
                )
                Spacer(Modifier.height(7.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(
                        modifier = Modifier.weight(1f).clipToBounds(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        PriorityBadge(item.priority)
                        item.tags.forEach { tag ->
                            Text(
                                text = tag,
                                fontSize = 12.sp,
                                color = TodoColors.Text2,
                                maxLines = 1,
                                modifier = Modifier
                                    .clip(RoundedCornerShape(6.dp))
                                    .background(TodoColors.Chip)
                                    .padding(horizontal = 8.dp, vertical = 2.dp)
                            )
                        }
                        if (row.carriedDays > 0L) {
                            Text(
                                text = "顺延 ${row.carriedDays} 天",
                                fontSize = 12.sp,
                                color = TodoColors.AccentDark,
                                maxLines = 1,
                                modifier = Modifier
                                    .clip(RoundedCornerShape(6.dp))
                                    .background(TodoColors.AccentLight)
                                    .padding(horizontal = 8.dp, vertical = 2.dp)
                            )
                        }
                    }
                    // 提示只在真被截断时出现；不占标题宽度，也不会多出一行。
                    if (overflowed) {
                        Spacer(Modifier.width(6.dp))
                        Text(
                            text = "全文",
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Medium,
                            color = TodoColors.AccentDark,
                            maxLines = 1
                        )
                    }
                }
            }

            if (showActions) {
                IconButton(onClick = onActions, modifier = Modifier.size(40.dp)) {
                    Icon(
                        Icons.Filled.MoreVert,
                        contentDescription = "更多操作",
                        tint = TodoColors.Text3,
                        modifier = Modifier.size(18.dp)
                    )
                }
            }
        }
    }
}

/** 色点 + 文字，颜色不是唯一的信息载体。 */
@Composable
private fun PriorityBadge(priority: Int) {
    val (label, fg, bg) = when (priority) {
        2 -> Triple("高", TodoColors.High, TodoColors.HighBg)
        1 -> Triple("中", TodoColors.Mid, TodoColors.MidBg)
        else -> Triple("低", TodoColors.Low, TodoColors.LowBg)
    }
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(6.dp))
            .background(bg)
            .padding(horizontal = 8.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(Modifier.size(6.dp).clip(CircleShape).background(fg))
        Spacer(Modifier.width(5.dp))
        Text(label, fontSize = 12.sp, fontWeight = FontWeight.Medium, color = fg)
    }
}

/**
 * 任务全文卡片。手机上用居中对话框：一次明确的「进入查看」，不改变列表布局。
 * 标题外面套 SelectionContainer——长任务经常要整段复制出去。
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun TaskDetailDialog(row: TaskRow, onDismiss: () -> Unit) {
    val item = row.item

    // 系统默认对话框只有 320dp 左右，读长文本太窄；这里关掉默认宽度自己定。
    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false)
    ) {
        Box(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 22.dp),
            contentAlignment = Alignment.Center
        ) {
            Surface(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(18.dp),
                color = TodoColors.Surface,
                border = BorderStroke(1.dp, TodoColors.LineStrong),
                shadowElevation = 10.dp
            ) {
                Column {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(start = 18.dp, end = 6.dp, top = 8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        StatusPill(item.done)
                        Spacer(Modifier.weight(1f))
                        IconButton(onClick = onDismiss, modifier = Modifier.size(40.dp)) {
                            Icon(
                                Icons.Filled.Close,
                                contentDescription = "关闭",
                                tint = TodoColors.Text3,
                                modifier = Modifier.size(18.dp)
                            )
                        }
                    }

                    Column(
                        modifier = Modifier
                            .heightIn(max = 400.dp)
                            .verticalScroll(rememberScrollState())
                            .padding(start = 18.dp, end = 18.dp, top = 2.dp, bottom = 16.dp)
                    ) {
                        SelectionContainer {
                            Text(
                                text = item.title,
                                fontSize = 17.sp,
                                lineHeight = 26.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (item.done) TodoColors.Text3 else TodoColors.Text1,
                                textDecoration = if (item.done) TextDecoration.LineThrough else null
                            )
                        }
                        Spacer(Modifier.height(12.dp))
                        FlowRow(
                            horizontalArrangement = Arrangement.spacedBy(6.dp),
                            verticalArrangement = Arrangement.spacedBy(6.dp)
                        ) {
                            PriorityBadge(item.priority)
                            item.tags.forEach { tag ->
                                Text(
                                    text = tag,
                                    fontSize = 12.sp,
                                    color = TodoColors.Text2,
                                    modifier = Modifier
                                        .clip(RoundedCornerShape(6.dp))
                                        .background(TodoColors.Chip)
                                        .padding(horizontal = 8.dp, vertical = 2.dp)
                                )
                            }
                            if (row.carriedDays > 0L) {
                                Text(
                                    text = "顺延 ${row.carriedDays} 天",
                                    fontSize = 12.sp,
                                    color = TodoColors.AccentDark,
                                    modifier = Modifier
                                        .clip(RoundedCornerShape(6.dp))
                                        .background(TodoColors.AccentLight)
                                        .padding(horizontal = 8.dp, vertical = 2.dp)
                                )
                            }
                        }
                        Spacer(Modifier.height(14.dp))
                        HorizontalDivider(thickness = 0.5.dp, color = TodoColors.Line)
                        Spacer(Modifier.height(10.dp))
                        Text(
                            text = "创建于 ${createdLabel(item.createdAt)} · 计划在 ${Dates.label(item.date)}",
                            fontSize = 12.sp,
                            lineHeight = 18.sp,
                            color = TodoColors.Text3
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun StatusPill(done: Boolean) {
    Text(
        text = if (done) "已完成" else "待完成",
        fontSize = 12.sp,
        fontWeight = FontWeight.Medium,
        color = if (done) TodoColors.Text2 else TodoColors.AccentDark,
        modifier = Modifier
            .clip(RoundedCornerShape(6.dp))
            .background(if (done) TodoColors.Chip else TodoColors.AccentLight)
            .padding(horizontal = 8.dp, vertical = 3.dp)
    )
}

@Composable
private fun NoteSection(
    vm: MainViewModel,
    isToday: Boolean,
    isPast: Boolean,
    onActions: (NoteItem) -> Unit,
    onOpenReference: (NoteReference) -> Unit
) {
    if (vm.notes.isEmpty()) {
        EmptyState(
            icon = Icons.Filled.Description,
            title = if (isToday) "今天还没有随记" else "这一天还没有随记",
            hint = when {
                isPast -> "换个日期看看"
                !isToday -> "随记只能记当天，回到今天再写"
                else -> "点右下角的 + 把此刻记下来"
            }
        )
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 12.dp, end = 12.dp, top = 8.dp, bottom = 104.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        items(vm.notes, key = { it.id }) { note ->
            NoteCardView(
                note = note,
                relation = if (isToday) "今天" else if (isPast) "历史" else "计划",
                editable = isToday,
                referenceLabel = { ref -> "引用 ${dateTitle(ref.date)} · ${vm.noteText(ref).take(12)}" },
                onActions = { onActions(note) },
                onOpenReference = onOpenReference
            )
        }
    }
}

@Composable
private fun NoteCardView(
    note: NoteItem,
    relation: String,
    editable: Boolean,
    referenceLabel: (NoteReference) -> String,
    onActions: () -> Unit,
    onOpenReference: (NoteReference) -> Unit
) {
    var expanded by rememberSaveable(note.id) { mutableStateOf(false) }
    var overflowing by remember(note.id) { mutableStateOf(false) }

    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        color = TodoColors.Surface,
        border = BorderStroke(1.dp, TodoColors.Line)
    ) {
        Column(Modifier.padding(start = 14.dp, end = 8.dp, top = 10.dp, bottom = 14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = timeLabel(note.createdAt),
                    fontSize = 12.sp,
                    color = TodoColors.Text3
                )
                Spacer(Modifier.width(8.dp))
                Surface(color = TodoColors.AccentLight, shape = CircleShape) {
                    Text(
                        text = relation,
                        fontSize = 11.sp,
                        color = TodoColors.AccentDark,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp)
                    )
                }
                Spacer(Modifier.weight(1f))
                if (editable) {
                    IconButton(onClick = onActions, modifier = Modifier.size(40.dp)) {
                        Icon(
                            Icons.Filled.MoreVert,
                            contentDescription = "更多操作",
                            tint = TodoColors.Text3,
                            modifier = Modifier.size(18.dp)
                        )
                    }
                }
            }

            Text(
                text = note.text,
                fontSize = 15.sp,
                lineHeight = 24.sp,
                color = TodoColors.Text1,
                maxLines = if (expanded) Int.MAX_VALUE else 3,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(end = 6.dp)
                    .clickable(enabled = overflowing || expanded) { expanded = !expanded },
                onTextLayout = { if (!expanded) overflowing = it.hasVisualOverflow }
            )

            if (overflowing || expanded) {
                Text(
                    text = if (expanded) "收起" else "展开全文",
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Medium,
                    color = TodoColors.AccentDark,
                    modifier = Modifier
                        .padding(top = 6.dp)
                        .clip(RoundedCornerShape(6.dp))
                        .clickable { expanded = !expanded }
                        .padding(horizontal = 2.dp, vertical = 4.dp)
                )
            }

            if (note.references.isNotEmpty()) {
                Spacer(Modifier.height(8.dp))
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    note.references.forEach { reference ->
                        Row(
                            modifier = Modifier
                                .clip(RoundedCornerShape(8.dp))
                                .background(TodoColors.AccentLight)
                                .clickable { onOpenReference(reference) }
                                .padding(horizontal = 9.dp, vertical = 6.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(
                                text = referenceLabel(reference),
                                fontSize = 12.sp,
                                color = TodoColors.AccentDark,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun EmptyState(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    title: String,
    hint: String
) {
    Column(
        modifier = Modifier.fillMaxSize().padding(bottom = 80.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Box(
            Modifier
                .size(56.dp)
                .clip(RoundedCornerShape(18.dp))
                .background(TodoColors.AccentLight),
            contentAlignment = Alignment.Center
        ) {
            Icon(icon, contentDescription = null, tint = TodoColors.AccentDark, modifier = Modifier.size(26.dp))
        }
        Spacer(Modifier.height(14.dp))
        Text(title, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = TodoColors.Text1)
        Spacer(Modifier.height(6.dp))
        Text(
            hint,
            fontSize = 13.sp,
            color = TodoColors.Text3,
            textAlign = TextAlign.Center
        )
    }
}
