@file:OptIn(ExperimentalFoundationApi::class, ExperimentalMaterial3Api::class)

package com.daytodo.android.ui

import android.Manifest
import android.app.TimePickerDialog
import android.os.Build
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.DateRange
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerState
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.daytodo.android.model.Dates
import com.daytodo.android.model.NoteItem
import com.daytodo.android.model.NoteReference
import com.daytodo.android.model.TaskItem
import java.time.Instant
import java.time.YearMonth
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import kotlinx.coroutines.launch

@Composable
fun DayTodoApp(vm: MainViewModel = viewModel()) {
    var showSettings by rememberSaveable { mutableStateOf(false) }
    var showDateSheet by remember { mutableStateOf(false) }
    var taskActions by remember { mutableStateOf<TaskItem?>(null) }
    var noteActions by remember { mutableStateOf<NoteItem?>(null) }
    var showAddSheet by remember { mutableStateOf(false) }
    var rescheduleTask by remember { mutableStateOf<TaskItem?>(null) }
    var toast by remember { mutableStateOf<String?>(null) }

    if (showSettings) {
        SettingsScreen(
            vm = vm,
            onBack = { showSettings = false },
            onToast = { toast = it }
        )
    } else {
        MainScreen(
            vm = vm,
            onSettings = { showSettings = true },
            onDatePicker = { showDateSheet = true },
            onTaskActions = { taskActions = it },
            onNoteActions = { noteActions = it },
            onAddSheet = { showAddSheet = true },
            onToast = { toast = it }
        )
    }

    if (showDateSheet) {
        ModalBottomSheet(onDismissRequest = { showDateSheet = false }) {
            DateSheet(vm = vm, onPick = { showDateSheet = false })
        }
    }

    taskActions?.let { task ->
        ModalBottomSheet(onDismissRequest = { taskActions = null }) {
            TaskActionsSheet(
                vm = vm,
                task = task,
                onDismiss = { taskActions = null },
                onReschedule = { rescheduleTask = it }
            )
        }
    }

    noteActions?.let { note ->
        ModalBottomSheet(onDismissRequest = { noteActions = null }) {
            NoteActionsSheet(
                vm = vm,
                note = note,
                onDismiss = { noteActions = null }
            )
        }
    }

    if (showAddSheet) {
        ModalBottomSheet(onDismissRequest = { showAddSheet = false }) {
            AddTaskSheet(
                vm = vm,
                onDismiss = { showAddSheet = false },
                onToast = { toast = it }
            )
        }
    }

    rescheduleTask?.let { task ->
        ModalBottomSheet(onDismissRequest = { rescheduleTask = null }) {
            RescheduleSheet(
                vm = vm,
                task = task,
                onDismiss = { rescheduleTask = null }
            )
        }
    }

    toast?.let { message ->
        ToastMessage(message) { toast = null }
    }
}

@Composable
private fun MainScreen(
    vm: MainViewModel,
    onSettings: () -> Unit,
    onDatePicker: () -> Unit,
    onTaskActions: (TaskItem) -> Unit,
    onNoteActions: (NoteItem) -> Unit,
    onAddSheet: () -> Unit,
    onToast: (String) -> Unit
) {
    Scaffold(
        topBar = {
            Surface(color = Color.White) {
                Column {
                    TopAppBar(
                        title = {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Box(
                                    Modifier
                                        .size(16.dp)
                                        .clip(RoundedCornerShape(5.dp))
                                        .background(Accent),
                                    contentAlignment = Alignment.Center
                                ) {
                                    Icon(
                                        Icons.Filled.Check,
                                        contentDescription = null,
                                        tint = Color.White,
                                        modifier = Modifier.size(11.dp)
                                    )
                                }
                                Spacer(Modifier.width(8.dp))
                                Text("DayTodo", fontSize = 15.sp, fontWeight = FontWeight.Medium)
                            }
                        },
                        actions = {
                            IconButton(onClick = onSettings) {
                                Icon(Icons.Filled.Settings, contentDescription = "设置", tint = TextSecondary)
                            }
                        },
                        colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.White)
                    )
                    TabRow(
                        selectedTabIndex = if (vm.tab == AppTab.Tasks) 0 else 1,
                        containerColor = Color.White,
                        contentColor = Accent
                    ) {
                        Tab(
                            selected = vm.tab == AppTab.Tasks,
                            onClick = { vm.updateTab(AppTab.Tasks) },
                            text = { Text("任务", fontSize = 12.sp) },
                            selectedContentColor = AccentDark,
                            unselectedContentColor = TextSecondary
                        )
                        Tab(
                            selected = vm.tab == AppTab.Notes,
                            onClick = { vm.updateTab(AppTab.Notes) },
                            text = { Text("随记", fontSize = 12.sp) },
                            selectedContentColor = AccentDark,
                            unselectedContentColor = TextSecondary
                        )
                    }
                    DateHeader(vm = vm, onDatePicker = onDatePicker)
                }
            }
        },
        bottomBar = { ComposerBar(vm = vm, onAddSheet = onAddSheet, onToast = onToast) },
        containerColor = Color.White
    ) { padding ->
        when (vm.tab) {
            AppTab.Tasks -> TaskList(vm, padding, onTaskActions)
            AppTab.Notes -> NoteList(vm, padding, onNoteActions)
        }
    }
}

@Composable
private fun DateHeader(vm: MainViewModel, onDatePicker: () -> Unit) {
    val isToday = vm.selectedDate == Dates.today()
    val relation = Dates.relation(vm.selectedDate)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(Color.White)
            .padding(horizontal = 10.dp, vertical = 9.dp),
        verticalAlignment = Alignment.Top
    ) {
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconButton(
                    onClick = { vm.moveDate(-1) },
                    modifier = Modifier.size(24.dp)
                ) {
                    Icon(
                        Icons.AutoMirrored.Filled.KeyboardArrowLeft,
                        contentDescription = "前一天",
                        tint = TextSecondary,
                        modifier = Modifier.size(18.dp)
                    )
                }
                Text(
                    Dates.label(vm.selectedDate),
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Medium,
                    color = TextPrimary
                )
                Spacer(Modifier.width(6.dp))
                Surface(
                    color = if (isToday) AccentLight else Chip,
                    shape = RoundedCornerShape(4.dp)
                ) {
                    Text(
                        relation,
                        fontSize = 10.sp,
                        color = if (isToday) AccentDark else TextSecondary,
                        modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                    )
                }
                IconButton(
                    onClick = { vm.moveDate(1) },
                    modifier = Modifier.size(24.dp)
                ) {
                    Icon(
                        Icons.AutoMirrored.Filled.KeyboardArrowRight,
                        contentDescription = "后一天",
                        tint = TextSecondary,
                        modifier = Modifier.size(18.dp)
                    )
                }
                if (!isToday) {
                    TextButton(
                        onClick = { vm.goToday() },
                        contentPadding = PaddingValues(horizontal = 4.dp, vertical = 0.dp)
                    ) {
                        Text("回到今天", fontSize = 10.sp, color = AccentDark)
                    }
                }
            }

            val summary = if (vm.tab == AppTab.Tasks) {
                "${vm.stats.done} / ${vm.stats.total} 项完成 · ${vm.stats.carry} 项顺延"
            } else {
                "${vm.stats.notes} 条随记 · 可引用今天的内容"
            }
            Text(summary, fontSize = 11.sp, color = TextMuted, modifier = Modifier.padding(top = 4.dp))

            if (vm.tab == AppTab.Tasks) {
                val progress = if (vm.stats.total == 0) 0f else vm.stats.done.toFloat() / vm.stats.total
                LinearProgressIndicator(
                    progress = { progress },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 7.dp)
                        .height(3.dp),
                    color = Accent,
                    trackColor = Line
                )
            }
        }
        IconButton(onClick = onDatePicker) {
            Icon(Icons.Filled.DateRange, contentDescription = "日历", tint = AccentDark)
        }
    }
}

@Composable
private fun TaskList(
    vm: MainViewModel,
    padding: PaddingValues,
    onActions: (TaskItem) -> Unit
) {
    if (vm.isLoading) {
        Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.Center) {
            CircularProgressIndicator(color = Accent)
        }
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(padding),
        contentPadding = PaddingValues(12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        if (vm.rows.isEmpty()) {
            item {
                Column(
                    Modifier.fillMaxWidth().padding(vertical = 48.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text("这一天还没有任务", fontSize = 13.sp, color = TextMuted)
                    Text(
                        if (vm.selectedDate == Dates.today()) "在下方输入框添加" else "换个日期看看",
                        fontSize = 11.sp,
                        color = TextMuted,
                        modifier = Modifier.padding(top = 5.dp)
                    )
                }
            }
        }
        items(vm.rows, key = { it.item.id }) { row ->
            TaskCard(row = row, vm = vm, onActions = onActions)
        }
    }
}

@Composable
private fun TaskCard(row: TaskRow, vm: MainViewModel, onActions: (TaskItem) -> Unit) {
    val item = row.item
    val today = Dates.today()
    val editable = vm.selectedDate == today
    val showActions = vm.selectedDate >= today
    val priorityColor = when (item.priority) {
        2 -> PriorityHigh
        1 -> PriorityNormal
        else -> PriorityLow
    }

    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(8.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        border = BorderStroke(0.5.dp, Line)
    ) {
        Row(Modifier.padding(10.dp), verticalAlignment = Alignment.Top) {
            Box(
                Modifier
                    .width(3.dp)
                    .height(42.dp)
                    .clip(RoundedCornerShape(2.dp))
                    .background(priorityColor)
            )
            Spacer(Modifier.width(8.dp))
            Box(
                Modifier
                    .size(20.dp)
                    .clip(CircleShape)
                    .background(if (item.done) Accent else Color.Transparent)
                    .border(1.dp, if (item.done) Accent else TextMuted, CircleShape)
                    .clickable(enabled = editable) { vm.toggleTask(item) },
                contentAlignment = Alignment.Center
            ) {
                if (item.done) {
                    Icon(
                        Icons.Filled.Check,
                        contentDescription = null,
                        tint = Color.White,
                        modifier = Modifier.size(12.dp)
                    )
                }
            }
            Spacer(Modifier.width(8.dp))
            Column(Modifier.weight(1f)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        item.title,
                        fontSize = 12.sp,
                        color = if (item.done) TextMuted else TextPrimary,
                        textDecoration = if (item.done) TextDecoration.LineThrough else null,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f, fill = false)
                    )
                    if (row.carriedDays > 0) {
                        Spacer(Modifier.width(4.dp))
                        Surface(color = AccentLight, shape = RoundedCornerShape(4.dp)) {
                            Text(
                                "顺延 ${row.carriedDays} 天",
                                fontSize = 10.sp,
                                color = AccentDark,
                                modifier = Modifier.padding(horizontal = 5.dp, vertical = 1.dp)
                            )
                        }
                    }
                }
                Row(Modifier.padding(top = 5.dp), verticalAlignment = Alignment.CenterVertically) {
                    item.tags.forEach { tag ->
                        Surface(color = Chip, shape = RoundedCornerShape(4.dp)) {
                            Text(
                                tag,
                                fontSize = 10.sp,
                                color = TextSecondary,
                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 1.dp)
                            )
                        }
                        Spacer(Modifier.width(4.dp))
                    }
                    Text(priorityLabel(item.priority), fontSize = 10.sp, color = TextMuted)
                }
            }
            if (showActions) {
                IconButton(onClick = { onActions(item) }, modifier = Modifier.size(28.dp)) {
                    Icon(
                        Icons.Filled.MoreVert,
                        contentDescription = "更多",
                        tint = TextMuted,
                        modifier = Modifier.size(18.dp)
                    )
                }
            }
        }
    }
}

@Composable
private fun NoteList(vm: MainViewModel, padding: PaddingValues, onActions: (NoteItem) -> Unit) {
    if (vm.isLoading) {
        Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.Center) {
            CircularProgressIndicator(color = Accent)
        }
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(padding),
        contentPadding = PaddingValues(12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        if (vm.notes.isEmpty()) {
            item {
                Column(
                    Modifier.fillMaxWidth().padding(vertical = 48.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text("这一天还没有随记", fontSize = 13.sp, color = TextMuted)
                    Text(
                        "把此刻的想法随手记下来",
                        fontSize = 11.sp,
                        color = TextMuted,
                        modifier = Modifier.padding(top = 5.dp)
                    )
                }
            }
        }
        items(vm.notes, key = { it.id }) { note ->
            NoteCard(note = note, vm = vm, onActions = onActions)
        }
    }
}

@Composable
private fun NoteCard(note: NoteItem, vm: MainViewModel, onActions: (NoteItem) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    val isLong = note.text.length > 70
    val showActions = vm.selectedDate >= Dates.today()

    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(8.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        border = BorderStroke(0.5.dp, Line)
    ) {
        Column(Modifier.padding(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(timeLabel(note.createdAt), fontSize = 11.sp, color = TextMuted)
                Spacer(Modifier.weight(1f))
                if (showActions) {
                    IconButton(onClick = { onActions(note) }, modifier = Modifier.size(28.dp)) {
                        Icon(Icons.Filled.MoreVert, "更多", tint = TextMuted, modifier = Modifier.size(18.dp))
                    }
                }
            }
            Text(
                note.text,
                fontSize = 12.sp,
                lineHeight = 19.sp,
                color = TextPrimary,
                maxLines = if (expanded || !isLong) Int.MAX_VALUE else 3,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 5.dp)
            )
            if (isLong) {
                TextButton(
                    onClick = { expanded = !expanded },
                    contentPadding = PaddingValues(0.dp),
                    modifier = Modifier.padding(top = 2.dp)
                ) {
                    Text(if (expanded) "收起" else "展开全文", fontSize = 11.sp, color = AccentDark)
                }
            }
            if (note.references.isNotEmpty()) {
                Row(
                    Modifier.padding(top = 7.dp),
                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    note.references.forEach { reference ->
                        Surface(
                            color = AccentLight,
                            shape = RoundedCornerShape(4.dp),
                            modifier = Modifier.clickable { vm.openReference(reference) }
                        ) {
                            Text(
                                "引用 ${Dates.label(reference.date)} · ${vm.noteText(reference).take(10)}",
                                fontSize = 10.sp,
                                color = AccentDark,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                modifier = Modifier.padding(horizontal = 7.dp, vertical = 3.dp)
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ComposerBar(
    vm: MainViewModel,
    onAddSheet: () -> Unit,
    onToast: (String) -> Unit
) {
    Surface(color = Panel, border = BorderStroke(0.5.dp, Line)) {
        when (vm.tab) {
            AppTab.Tasks -> TaskComposer(vm, onAddSheet)
            AppTab.Notes -> NoteComposer(vm, onToast)
        }
    }
}

@Composable
private fun TaskComposer(vm: MainViewModel, onAddSheet: () -> Unit) {
    val isToday = vm.selectedDate == Dates.today()
    Column(Modifier.padding(horizontal = 12.dp, vertical = 9.dp)) {
        if (vm.hasRecalledDraft) {
            Row(Modifier.padding(bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                Surface(color = AccentLight, shape = RoundedCornerShape(4.dp)) {
                    Text("撤回", fontSize = 10.sp, color = AccentDark, modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
                }
                Spacer(Modifier.width(6.dp))
                Text("改完回车重新发送", fontSize = 11.sp, color = TextSecondary)
                Spacer(Modifier.weight(1f))
                TextButton(
                    onClick = {
                        vm.hasRecalledDraft = false
                        vm.taskDraft = ""
                        vm.tagDraft = ""
                    },
                    contentPadding = PaddingValues(0.dp)
                ) {
                    Text("清除", fontSize = 11.sp)
                }
            }
        }

        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = vm.taskDraft,
                onValueChange = { vm.taskDraft = it },
                modifier = Modifier.weight(1f).height(56.dp),
                enabled = isToday,
                singleLine = true,
                placeholder = { Text("添加任务，回车保存", fontSize = 12.sp) },
                shape = RoundedCornerShape(6.dp),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { vm.addTask() }),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedBorderColor = Accent,
                    unfocusedBorderColor = Color(0xFFDCDAD2),
                    focusedContainerColor = Color.White,
                    unfocusedContainerColor = Color.White
                )
            )
            Spacer(Modifier.width(8.dp))
            Button(
                onClick = { vm.addTask() },
                enabled = isToday && vm.taskDraft.isNotBlank(),
                shape = RoundedCornerShape(6.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Accent)
            ) {
                Text("添加")
            }
            IconButton(onClick = onAddSheet) {
                Icon(Icons.Filled.Tune, contentDescription = "更多选项", tint = TextSecondary)
            }
        }

        Row(Modifier.padding(top = 7.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("标签", fontSize = 10.sp, color = TextMuted)
            Spacer(Modifier.width(6.dp))
            Text(
                if (vm.tagDraft.isBlank()) "—" else vm.tagDraft,
                fontSize = 10.sp,
                color = TextSecondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f)
            )
            Text("优先级", fontSize = 10.sp, color = TextMuted)
            Spacer(Modifier.width(4.dp))
            Text(priorityLabel(vm.priorityDraft), fontSize = 10.sp, color = AccentDark)
        }

        if (!isToday) {
            Text("这一天是历史记录，只能查看", fontSize = 10.sp, color = TextMuted, modifier = Modifier.padding(top = 4.dp))
        }
    }
}

@Composable
private fun NoteComposer(vm: MainViewModel, onToast: (String) -> Unit) {
    val isToday = vm.selectedDate == Dates.today()
    Column(Modifier.padding(horizontal = 12.dp, vertical = 9.dp)) {
        if (vm.editingNoteId != null) {
            Row(Modifier.padding(bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                Surface(color = AccentLight, shape = RoundedCornerShape(4.dp)) {
                    Text("编辑", fontSize = 10.sp, color = AccentDark, modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
                }
                Spacer(Modifier.width(6.dp))
                Text("保存后更新这条随记", fontSize = 11.sp, color = TextSecondary)
                Spacer(Modifier.weight(1f))
                TextButton(onClick = { vm.cancelNoteEdit() }, contentPadding = PaddingValues(0.dp)) {
                    Text("取消编辑", fontSize = 11.sp)
                }
            }
        }

        vm.pendingQuote?.let { quote ->
            Row(Modifier.padding(bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                Surface(color = AccentLight, shape = RoundedCornerShape(4.dp)) {
                    Text(
                        "引用 ${Dates.label(quote.date)} · ${vm.noteText(quote).take(10)}",
                        fontSize = 10.sp,
                        color = AccentDark,
                        modifier = Modifier.padding(horizontal = 7.dp, vertical = 3.dp)
                    )
                }
                Spacer(Modifier.width(6.dp))
                TextButton(onClick = { vm.clearQuote() }, contentPadding = PaddingValues(0.dp)) {
                    Text("清除引用", fontSize = 11.sp)
                }
            }
        }

        if (isToday) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = vm.noteDraft,
                    onValueChange = { vm.noteDraft = it },
                    modifier = Modifier.weight(1f).height(76.dp),
                    placeholder = { Text("随手记下现在的想法…", fontSize = 12.sp) },
                    shape = RoundedCornerShape(6.dp),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { vm.saveNote() }),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = Accent,
                        unfocusedBorderColor = Color(0xFFDCDAD2),
                        focusedContainerColor = Color.White,
                        unfocusedContainerColor = Color.White
                    )
                )
                Spacer(Modifier.width(8.dp))
                Button(
                    onClick = {
                        if (vm.noteDraft.isBlank()) onToast("先写点内容") else vm.saveNote()
                    },
                    modifier = Modifier.height(76.dp),
                    shape = RoundedCornerShape(6.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Accent)
                ) {
                    Text("保存")
                }
            }
            Text("Enter 保存 · Shift+Enter 换行", fontSize = 10.sp, color = TextMuted, modifier = Modifier.padding(top = 5.dp))
        } else {
            Text("这一天是历史记录，只能查看和引用", fontSize = 11.sp, color = TextMuted)
        }
    }
}

@Composable
private fun DateSheet(vm: MainViewModel, onPick: () -> Unit) {
    var month by remember { mutableStateOf(YearMonth.from(Dates.parse(vm.selectedDate))) }

    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("选择日期", fontSize = 15.sp, fontWeight = FontWeight.Medium)
            Spacer(Modifier.weight(1f))
            TextButton(onClick = { vm.goToday(); onPick() }) {
                Text("回到今天", fontSize = 12.sp, color = AccentDark)
            }
        }

        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { month = month.minusMonths(1) }) {
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowLeft, "上个月", tint = TextSecondary)
            }
            Text(
                "${month.year}年${month.monthValue}月",
                fontSize = 13.sp,
                color = TextPrimary,
                modifier = Modifier.weight(1f),
                textAlign = androidx.compose.ui.text.style.TextAlign.Center
            )
            IconButton(onClick = { month = month.plusMonths(1) }) {
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, "下个月", tint = TextSecondary)
            }
        }

        Row(Modifier.fillMaxWidth()) {
            listOf("一", "二", "三", "四", "五", "六", "日").forEach {
                Text(it, fontSize = 10.sp, color = TextMuted, modifier = Modifier.weight(1f), textAlign = androidx.compose.ui.text.style.TextAlign.Center)
            }
        }

        LazyVerticalGrid(columns = GridCells.Fixed(7), modifier = Modifier.height(232.dp)) {
            val leading = (month.atDay(1).dayOfWeek.value + 6) % 7
            items(leading) { Spacer(Modifier.size(36.dp)) }
            items(month.lengthOfMonth()) { index ->
                val date = month.atDay(index + 1)
                val value = Dates.format(date)
                val selected = value == vm.selectedDate
                val hasTask = vm.hasTasksOn(value)
                val hasNote = vm.hasNotesOn(value)
                Box(
                    modifier = Modifier
                        .size(36.dp)
                        .clip(CircleShape)
                        .background(if (selected) Accent else Color.Transparent)
                        .clickable {
                            vm.selectDate(value)
                            onPick()
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(
                            (index + 1).toString(),
                            fontSize = 11.sp,
                            color = if (selected) Color.White else TextPrimary
                        )
                        Row(
                            Modifier.padding(top = 1.dp),
                            horizontalArrangement = Arrangement.spacedBy(2.dp)
                        ) {
                            if (hasTask) Spacer(Modifier.size(3.dp).clip(CircleShape).background(if (selected) Color.White else Accent))
                            if (hasNote) Spacer(Modifier.size(3.dp).clip(CircleShape).background(PriorityNormal))
                        }
                    }
                }
            }
        }

        Row(Modifier.padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            Text("● 有任务", fontSize = 10.sp, color = TextSecondary)
            Text("● 有随记", fontSize = 10.sp, color = TextSecondary)
        }
    }
}

@Composable
private fun TaskActionsSheet(
    vm: MainViewModel,
    task: TaskItem,
    onDismiss: () -> Unit,
    onReschedule: (TaskItem) -> Unit
) {
    val today = Dates.today()
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("任务操作", fontSize = 15.sp, fontWeight = FontWeight.Medium)
            Spacer(Modifier.weight(1f))
            Text(
                task.title,
                fontSize = 11.sp,
                color = TextMuted,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f)
            )
        }
        Spacer(Modifier.height(8.dp))
        if (task.date != today) {
            ActionRow("拉回今天", Accent, onClick = {
                vm.bringToToday(task)
                onDismiss()
            })
        }
        if (task.date >= today || vm.selectedDate == today) {
            ActionRow("改期", PriorityNormal, onClick = {
                onDismiss()
                onReschedule(task)
            })
        }
        if (vm.selectedDate == today) {
            ActionRow("撤回", AccentDark, hint = "回填编辑框", onClick = {
                vm.recallTask(task)
                onDismiss()
            })
            ActionRow("删除", PriorityHigh, hint = "不可恢复", onClick = {
                vm.deleteTask(task)
                onDismiss()
            })
        }
        Spacer(Modifier.height(8.dp))
        OutlinedButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) {
            Text("取消")
        }
    }
}

@Composable
private fun NoteActionsSheet(vm: MainViewModel, note: NoteItem, onDismiss: () -> Unit) {
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("随记操作", fontSize = 15.sp, fontWeight = FontWeight.Medium)
            Spacer(Modifier.weight(1f))
            Text(
                timeLabel(note.createdAt),
                fontSize = 11.sp,
                color = TextMuted
            )
        }
        Spacer(Modifier.height(8.dp))
        ActionRow("引用", Accent, hint = "跳回今天", onClick = {
            vm.quoteNote(note)
            onDismiss()
        })
        if (vm.selectedDate == Dates.today()) {
            ActionRow("编辑", AccentDark, onClick = {
                vm.editNote(note)
                onDismiss()
            })
            ActionRow("删除", PriorityHigh, hint = "不可恢复", onClick = {
                vm.deleteNote(note)
                onDismiss()
            })
        }
        Spacer(Modifier.height(8.dp))
        OutlinedButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) {
            Text("取消")
        }
    }
}

@Composable
private fun ActionRow(
    label: String,
    color: Color,
    hint: String? = null,
    onClick: () -> Unit
) {
    Column {
        Row(
            Modifier
                .fillMaxWidth()
                .height(44.dp)
                .clickable(onClick = onClick),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Spacer(Modifier.size(7.dp).clip(CircleShape).background(color))
            Spacer(Modifier.width(9.dp))
            Text(
                label,
                fontSize = 13.sp,
                color = if (color == PriorityHigh) PriorityHigh else TextPrimary,
                modifier = Modifier.weight(1f)
            )
            if (hint != null) Text(hint, fontSize = 10.sp, color = TextMuted)
        }
        HorizontalDivider(color = Line)
    }
}

@Composable
private fun AddTaskSheet(
    vm: MainViewModel,
    onDismiss: () -> Unit,
    onToast: (String) -> Unit
) {
    Column(Modifier.fillMaxWidth().padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("添加任务", fontSize = 15.sp, fontWeight = FontWeight.Medium)
            Spacer(Modifier.weight(1f))
            TextButton(onClick = onDismiss) { Text("取消", fontSize = 12.sp) }
        }
        Spacer(Modifier.height(8.dp))
        OutlinedTextField(
            value = vm.taskDraft,
            onValueChange = { vm.taskDraft = it },
            modifier = Modifier.fillMaxWidth().height(56.dp),
            singleLine = true,
            placeholder = { Text("要做什么？", fontSize = 12.sp) },
            shape = RoundedCornerShape(6.dp)
        )
        Spacer(Modifier.height(10.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("标签", fontSize = 11.sp, color = TextMuted, modifier = Modifier.width(36.dp))
            OutlinedTextField(
                value = vm.tagDraft,
                onValueChange = { vm.tagDraft = it },
                modifier = Modifier.weight(1f).height(56.dp),
                singleLine = true,
                placeholder = { Text("用逗号分隔，最多 4 个", fontSize = 11.sp) },
                shape = RoundedCornerShape(6.dp)
            )
        }
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("优先级", fontSize = 11.sp, color = TextMuted, modifier = Modifier.width(36.dp))
            listOf(2 to "高", 1 to "中", 0 to "低").forEach { (value, label) ->
                FilterChip(
                    selected = vm.priorityDraft == value,
                    onClick = { vm.priorityDraft = value },
                    label = { Text(label, fontSize = 11.sp) },
                    modifier = Modifier.padding(end = 6.dp)
                )
            }
        }
        Spacer(Modifier.height(14.dp))
        Button(
            onClick = {
                if (vm.taskDraft.isBlank()) {
                    onToast("先写点内容")
                } else {
                    vm.addTask()
                    onDismiss()
                }
            },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(6.dp),
            colors = ButtonDefaults.buttonColors(containerColor = Accent)
        ) {
            Text("添加任务")
        }
    }
}

@Composable
private fun RescheduleSheet(
    vm: MainViewModel,
    task: TaskItem,
    onDismiss: () -> Unit
) {
    val initialMillis = Dates.parse(task.date).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
    val state: DatePickerState = rememberDatePickerState(initialSelectedDateMillis = initialMillis)

    Column(Modifier.fillMaxWidth().padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("改期", fontSize = 15.sp, fontWeight = FontWeight.Medium)
            Spacer(Modifier.weight(1f))
            Text(task.title, fontSize = 11.sp, color = TextMuted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            listOf(
                "今天" to Dates.today(),
                "明天" to Dates.shift(Dates.today(), 1),
                "7天后" to Dates.shift(Dates.today(), 7)
            ).forEach { (label, value) ->
                TextButton(onClick = {
                    vm.rescheduleTask(task, value)
                    onDismiss()
                }) {
                    Text(label, fontSize = 11.sp, color = AccentDark)
                }
            }
        }
        DatePicker(state = state, showModeToggle = false)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
            TextButton(onClick = onDismiss) { Text("取消") }
            Spacer(Modifier.width(8.dp))
            Button(
                onClick = {
                    val millis = state.selectedDateMillis ?: return@Button
                    val date = Instant.ofEpochMilli(millis)
                        .atZone(ZoneOffset.UTC)
                        .toLocalDate()
                        .format(DateTimeFormatter.ISO_LOCAL_DATE)
                    vm.rescheduleTask(task, date)
                    onDismiss()
                },
                colors = ButtonDefaults.buttonColors(containerColor = Accent)
            ) {
                Text("确定")
            }
        }
    }
}

@Composable
private fun SettingsScreen(
    vm: MainViewModel,
    onBack: () -> Unit,
    onToast: (String) -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) vm.updateReminderEnabled(true) else onToast("需要通知权限才能提醒")
    }

    val exportLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("application/json")
    ) { uri ->
        if (uri != null) {
            scope.launch {
                try {
                    context.contentResolver.openOutputStream(uri)?.use { stream ->
                        stream.write(vm.exportJson().toByteArray())
                    }
                    onToast("已导出")
                } catch (_: Exception) {
                    onToast("导出失败")
                }
            }
        }
    }

    val importLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri ->
        if (uri != null) {
            scope.launch {
                try {
                    val text = context.contentResolver.openInputStream(uri)?.bufferedReader()?.use { it.readText() }
                    if (text != null) {
                        vm.importJson(text) { ok -> onToast(if (ok) "已导入" else "文件格式不对") }
                    }
                } catch (_: Exception) {
                    onToast("导入失败")
                }
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("设置", fontSize = 15.sp) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回", tint = TextSecondary)
                    }
                },
                actions = {
                    TextButton(onClick = onBack) { Text("完成", color = AccentDark) }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.White)
            )
        },
        containerColor = Panel
    ) { padding ->
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(12.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            item {
                SettingCard("通用") {
                    ToggleRow(
                        label = "每日提醒",
                        hint = "每天 ${vm.reminderTime} 提醒查看清单",
                        checked = vm.reminderEnabled
                    ) { enabled ->
                        if (enabled && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                            permissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                        } else {
                            vm.updateReminderEnabled(enabled)
                        }
                    }
                    HorizontalDivider(color = Line)
                    val parts = vm.reminderTime.split(":").mapNotNull { it.toIntOrNull() }
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .height(50.dp)
                            .clickable {
                                val hour = parts.getOrNull(0) ?: 8
                                val minute = parts.getOrNull(1) ?: 30
                                TimePickerDialog(context, { _, h, m ->
                                    vm.updateReminderTime("%02d:%02d".format(h, m))
                                }, hour, minute, true).show()
                            },
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text("提醒时间", fontSize = 12.sp)
                            Text("点击修改", fontSize = 10.sp, color = TextMuted)
                        }
                        Text(vm.reminderTime, fontSize = 12.sp, color = AccentDark)
                    }
                    HorizontalDivider(color = Line)
                    ToggleRow(
                        label = "自动顺延未完成任务",
                        hint = "跨天时保留原日期记录",
                        checked = vm.autoCarry
                    ) { vm.updateAutoCarry(it) }
                }
            }

            item {
                SettingCard("数据") {
                    InfoRow("本地存储", vm.dataFileInfo().takeLast(46))
                    HorizontalDivider(color = Line)
                    SettingAction("导出备份") { exportLauncher.launch("daytodo-backup.json") }
                    HorizontalDivider(color = Line)
                    SettingAction("导入备份") { importLauncher.launch(arrayOf("application/json", "text/plain")) }
                }
            }

            item {
                SettingCard("关于") {
                    Text("DayTodo Android 1.0", fontSize = 12.sp)
                    Text(
                        "按天显示的轻量清单，未完成任务自动顺延到今天。",
                        fontSize = 11.sp,
                        color = TextMuted,
                        modifier = Modifier.padding(top = 5.dp)
                    )
                }
            }
        }
    }
}

@Composable
private fun SettingCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(10.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        border = BorderStroke(0.5.dp, Line)
    ) {
        Column(Modifier.fillMaxWidth().padding(14.dp)) {
            Text(title, fontSize = 11.sp, color = TextMuted, modifier = Modifier.padding(bottom = 8.dp))
            content()
        }
    }
}

@Composable
private fun ToggleRow(
    label: String,
    hint: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit
) {
    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(label, fontSize = 12.sp)
            Text(hint, fontSize = 10.sp, color = TextMuted, modifier = Modifier.padding(top = 3.dp))
        }
        Switch(
            checked = checked,
            onCheckedChange = onChange,
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color.White,
                checkedTrackColor = Accent,
                uncheckedThumbColor = Color.White,
                uncheckedTrackColor = PriorityLow
            )
        )
    }
}

@Composable
private fun InfoRow(label: String, value: String) {
    Column(Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
        Text(label, fontSize = 12.sp)
        Text(value, fontSize = 10.sp, color = TextMuted, modifier = Modifier.padding(top = 3.dp))
    }
}

@Composable
private fun SettingAction(label: String, onClick: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().height(44.dp).clickable(onClick = onClick),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, fontSize = 12.sp, modifier = Modifier.weight(1f))
        Text("›", fontSize = 14.sp, color = TextMuted)
    }
}

@Composable
private fun ToastMessage(text: String, onDone: () -> Unit) {
    val context = LocalContext.current
    LaunchedEffect(text) {
        Toast.makeText(context, text, Toast.LENGTH_SHORT).show()
        onDone()
    }
}

private fun priorityLabel(priority: Int): String = when (priority) {
    2 -> "高优先级"
    1 -> "中优先级"
    else -> "低优先级"
}

private fun timeLabel(createdAt: Long): String =
    Instant.ofEpochMilli(createdAt)
        .atZone(ZoneId.systemDefault())
        .toLocalTime()
        .format(DateTimeFormatter.ofPattern("HH:mm"))
