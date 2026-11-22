package com.daytodo.android.ui

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.daytodo.android.model.Dates
import com.daytodo.android.model.NoteItem
import com.daytodo.android.model.TaskItem
import kotlinx.coroutines.launch

@Composable
fun DayTodoApp(vm: MainViewModel = viewModel()) {
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

    var showSettings by rememberSaveable { mutableStateOf(false) }
    var showAddTask by remember { mutableStateOf(false) }
    var showNoteComposer by remember { mutableStateOf(false) }
    var editingNote by remember { mutableStateOf<NoteItem?>(null) }
    var showDateSheet by remember { mutableStateOf(false) }
    var rescheduleTask by remember { mutableStateOf<TaskItem?>(null) }
    var taskActions by remember { mutableStateOf<TaskItem?>(null) }
    var noteActions by remember { mutableStateOf<NoteItem?>(null) }

    fun notify(message: String, actionLabel: String? = null, onAction: (() -> Unit)? = null) {
        scope.launch {
            snackbarHostState.currentSnackbarData?.dismiss()
            val result = snackbarHostState.showSnackbar(
                message = message,
                actionLabel = actionLabel,
                duration = SnackbarDuration.Short
            )
            if (result == SnackbarResult.ActionPerformed) onAction?.invoke()
        }
    }

    val snackbarHost: @Composable () -> Unit = {
        SnackbarHost(hostState = snackbarHostState) { data ->
            Snackbar(
                snackbarData = data,
                shape = RoundedCornerShape(12.dp),
                containerColor = TodoColors.Snack,
                contentColor = Color.White,
                actionContentColor = TodoColors.SnackAction,
                dismissActionContentColor = Color.White
            )
        }
    }

    if (showSettings) {
        SettingsScreen(
            vm = vm,
            snackbarHost = snackbarHost,
            onBack = { showSettings = false },
            onToast = { notify(it) }
        )
    } else {
        MainScreen(
            vm = vm,
            snackbarHost = snackbarHost,
            onOpenCalendar = { showDateSheet = true },
            onAddTask = { showAddTask = true },
            onAddNote = { showNoteComposer = true },
            onTaskActions = { taskActions = it },
            onNoteActions = { noteActions = it },
            onOpenReference = { vm.openReference(it) },
            onSettings = { showSettings = true },
            onClearCompleted = {
                vm.clearCompleted()
                notify("已清除已完成任务", "撤销") { vm.undo() }
            }
        )
    }

    if (showAddTask) {
        AddTaskSheet(
            dateLabel = dateTitle(vm.selectedDate),
            isRecall = vm.hasRecalledDraft,
            initialTitle = vm.taskDraft,
            initialTags = vm.tagsFrom(vm.tagDraft),
            initialPriority = vm.priorityDraft,
            onDismiss = {
                showAddTask = false
                vm.clearRecallDraft()
            },
            onSave = { title, tags, priority ->
                vm.addTask(title, tags, priority)
                showAddTask = false
                notify(if (vm.selectedDate == Dates.today()) "已添加任务" else "已添加到 ${dateTitle(vm.selectedDate)}")
            }
        )
    }

    if (showDateSheet) {
        DateSheet(
            selected = vm.selectedDate,
            onPick = { date ->
                vm.selectDate(date)
                showDateSheet = false
            },
            onDismiss = { showDateSheet = false }
        )
    }

    rescheduleTask?.let { task ->
        DateSheet(
            selected = task.date,
            title = "改期到",
            onPick = { date ->
                vm.rescheduleTask(task, date)
                rescheduleTask = null
                notify("已改期到 ${dateTitle(date)}")
            },
            onDismiss = { rescheduleTask = null }
        )
    }

    taskActions?.let { task ->
        val today = Dates.today()
        TaskActionsSheet(
            task = task,
            canPullToToday = task.date < today && !task.done,
            canEdit = vm.selectedDate >= today,
            canReschedule = vm.selectedDate >= today,
            onPullToToday = {
                vm.bringToToday(task)
                taskActions = null
                notify("已拉回今天")
            },
            onReschedule = {
                taskActions = null
                rescheduleTask = task
            },
            onRecall = {
                vm.recallTask(task)
                taskActions = null
                showAddTask = true
            },
            onDelete = {
                vm.deleteTask(task)
                taskActions = null
                notify("已删除任务", "撤销") { vm.undo() }
            },
            onDismiss = { taskActions = null }
        )
    }

    noteActions?.let { note ->
        NoteActionsSheet(
            preview = note.text.take(28),
            canEdit = vm.selectedDate == Dates.today(),
            onQuote = {
                vm.quoteNote(note)
                noteActions = null
                showNoteComposer = true
            },
            onEdit = {
                vm.editNote(note)
                noteActions = null
                editingNote = note
            },
            onDelete = {
                vm.deleteNote(note)
                noteActions = null
                notify("已删除随记", "撤销") { vm.undo() }
            },
            onDismiss = { noteActions = null }
        )
    }

    if (showNoteComposer || editingNote != null) {
        val target = editingNote
        NoteComposerSheet(
            initialText = target?.text ?: vm.noteDraft,
            isEditing = target != null,
            quoteLabel = vm.pendingQuote?.let {
                "引用 ${dateTitle(it.date)} · ${vm.noteText(it).take(14)}"
            },
            onClearQuote = { vm.clearQuote() },
            onDismiss = {
                showNoteComposer = false
                editingNote = null
                vm.cancelNoteEdit()
            },
            onSave = { text ->
                if (target != null) vm.updateNote(target, text) else vm.addNote(text)
                showNoteComposer = false
                editingNote = null
                notify(if (target != null) "已更新随记" else "已保存随记")
            }
        )
    }
}
