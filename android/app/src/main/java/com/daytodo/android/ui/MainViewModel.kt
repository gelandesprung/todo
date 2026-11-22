package com.daytodo.android.ui

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.daytodo.android.data.ReminderScheduler
import com.daytodo.android.data.Repository
import com.daytodo.android.model.AppData
import com.daytodo.android.model.Dates
import com.daytodo.android.model.DayData
import com.daytodo.android.model.NoteItem
import com.daytodo.android.model.NoteReference
import com.daytodo.android.model.TaskItem
import com.daytodo.android.model.newNoteId
import com.daytodo.android.model.newTaskId
import kotlinx.coroutines.launch

enum class AppTab {
    Tasks,
    Notes
}

data class TaskRow(
    val item: TaskItem,
    val carriedDays: Long = 0
)

/** 可撤销的操作快照。只保留最近一次，和桌面版「只留最后一条撤回」的行为一致。 */
sealed interface UndoOp {
    data class Tasks(val items: List<TaskItem>) : UndoOp
    data class Note(val item: NoteItem) : UndoOp
}

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = Repository(application)

    private var _data by mutableStateOf(AppData())

    var isLoading by mutableStateOf(true)
        private set

    var reminderEnabled by mutableStateOf(false)
        private set

    var reminderTime by mutableStateOf("08:30")
        private set

    var autoCarry by mutableStateOf(true)
        private set

    var selectedDate by mutableStateOf(Dates.today())
        private set

    var tab by mutableStateOf(AppTab.Tasks)
        private set

    var rows by mutableStateOf<List<TaskRow>>(emptyList())
        private set

    /** 分组结果在 refresh() 里算一次，界面直接用，不在 composable 里过滤。 */
    var carriedRows by mutableStateOf<List<TaskRow>>(emptyList())
        private set

    var openRows by mutableStateOf<List<TaskRow>>(emptyList())
        private set

    var doneRows by mutableStateOf<List<TaskRow>>(emptyList())
        private set

    var notes by mutableStateOf<List<NoteItem>>(emptyList())
        private set

    var taskDraft by mutableStateOf("")
    var tagDraft by mutableStateOf("")
    var priorityDraft by mutableStateOf(1)
    var hasRecalledDraft by mutableStateOf(false)

    /** 最近一次可撤销操作，null 表示没有可撤销的内容。 */
    var lastUndo by mutableStateOf<UndoOp?>(null)
        private set

    var noteDraft by mutableStateOf("")
    var editingNoteId by mutableStateOf<String?>(null)
    var pendingQuote by mutableStateOf<NoteReference?>(null)

    init {
        viewModelScope.launch {
            _data = repo.load()
            syncSettingsState()
            isLoading = false
            refresh()
            ReminderScheduler.sync(getApplication(), reminderEnabled, reminderTime)
        }
    }

    fun dataFileInfo(): String = repo.dataFileInfo()

    suspend fun exportJson(): String = repo.exportJson()

    fun importJson(text: String, onResult: (Boolean) -> Unit) {
        viewModelScope.launch {
            try {
                val parsed = repo.importJson(text)
                _data = parsed
                syncSettingsState()
                repo.save(parsed)
                selectedDate = Dates.today()
                tab = AppTab.Tasks
                resetTransient()
                refresh()
                ReminderScheduler.sync(getApplication(), reminderEnabled, reminderTime)
                onResult(true)
            } catch (_: Exception) {
                onResult(false)
            }
        }
    }

    fun updateTab(value: AppTab) {
        tab = value
    }

    fun selectDate(value: String) {
        selectedDate = value
        resetTransient()
        refresh()
    }

    fun moveDate(days: Long) = selectDate(Dates.shift(selectedDate, days))

    fun goToday() = selectDate(Dates.today())

    fun addTask() {
        addTask(taskDraft, parseTags(tagDraft), priorityDraft)
    }

    /**
     * 新面板的入口：标题、标签、优先级一次给全，不再依赖全局草稿字段。
     * 今天和未来日期都能新增，只有历史日期是只读的。
     */
    fun addTask(title: String, tags: List<String>, priority: Int) {
        val text = title.trim()
        if (text.isEmpty() || selectedDate < Dates.today()) return
        dayData(selectedDate).tasks.add(
            TaskItem(
                id = newTaskId(),
                date = selectedDate,
                title = text,
                tags = tags.map { it.trim() }.filter { it.isNotEmpty() }.distinct().take(4),
                priority = priority
            )
        )
        clearRecallDraft()
        commit()
    }

    /** 清掉撤回草稿，面板关闭或保存后调用。 */
    fun clearRecallDraft() {
        hasRecalledDraft = false
        taskDraft = ""
        tagDraft = ""
        priorityDraft = 1
    }

    /** 把逗号分隔的标签串解析成列表，供面板初始化用。 */
    fun tagsFrom(raw: String): List<String> = parseTags(raw)

    /** 清除当前选中日的已完成任务，可撤销。 */
    fun clearCompleted() {
        val day = _data.days[selectedDate] ?: return
        val removed = day.tasks.filter { it.done }
        if (removed.isEmpty()) return
        day.tasks.removeAll { it.done }
        lastUndo = UndoOp.Tasks(removed)
        commit()
    }

    /** 撤销最近一次删除 / 清除。 */
    fun undo() {
        when (val op = lastUndo) {
            is UndoOp.Tasks -> op.items.forEach { dayData(it.date).tasks.add(it) }
            is UndoOp.Note -> dayData(op.item.date).notes.add(op.item)
            null -> return
        }
        lastUndo = null
        commit()
    }

    fun toggleTask(item: TaskItem) {
        if (selectedDate < Dates.today()) return
        updateTask(item.id) { it.copy(done = !it.done) }
    }

    fun recallTask(item: TaskItem) {
        if (selectedDate < Dates.today()) return
        val task = removeTask(item.id) ?: return
        taskDraft = task.title
        tagDraft = task.tags.joinToString(", ")
        priorityDraft = task.priority
        hasRecalledDraft = true
        commit()
    }

    fun bringToToday(item: TaskItem) = moveTask(item.id, Dates.today())

    fun rescheduleTask(item: TaskItem, date: String) = moveTask(item.id, date)

    fun deleteTask(item: TaskItem) {
        val removed = removeTask(item.id) ?: return
        lastUndo = UndoOp.Tasks(listOf(removed))
        commit()
    }

    /** 面板入口：文本直接传入，不再走全局草稿字段。 */
    fun addNote(text: String) {
        val t = text.trim()
        if (t.isEmpty() || selectedDate != Dates.today()) return
        dayData(selectedDate).notes.add(
            NoteItem(
                id = newNoteId(),
                date = selectedDate,
                createdAt = System.currentTimeMillis(),
                text = t,
                references = listOfNotNull(pendingQuote)
            )
        )
        pendingQuote = null
        noteDraft = ""
        editingNoteId = null
        commit()
    }

    /** 编辑保存：保留 Id / Date / CreatedAt，只换文本并合并引用。 */
    fun updateNote(note: NoteItem, text: String) {
        val t = text.trim()
        if (t.isEmpty()) return
        val day = dayData(note.date)
        val index = day.notes.indexOfFirst { it.id == note.id }
        if (index < 0) return
        val old = day.notes[index]
        val merged = (old.references + listOfNotNull(pendingQuote)).distinctBy { it.noteId }
        day.notes[index] = old.copy(text = t, references = merged)
        pendingQuote = null
        noteDraft = ""
        editingNoteId = null
        commit()
    }

    fun editNote(note: NoteItem) {
        editingNoteId = note.id
        noteDraft = note.text
        pendingQuote = null
    }

    fun cancelNoteEdit() {
        editingNoteId = null
        noteDraft = ""
    }

    fun deleteNote(note: NoteItem) {
        val day = dayData(note.date)
        day.notes.removeAll { it.id == note.id }
        lastUndo = UndoOp.Note(note)
        if (editingNoteId == note.id) cancelNoteEdit()
        commit()
    }

    fun quoteNote(note: NoteItem) {
        pendingQuote = NoteReference(note.date, note.id, note.text)
        selectedDate = Dates.today()
        tab = AppTab.Notes
        editingNoteId = null
        noteDraft = ""
        refresh()
    }

    fun clearQuote() {
        pendingQuote = null
    }

    fun openReference(reference: NoteReference) {
        selectedDate = reference.date
        tab = AppTab.Notes
        resetTransient()
        refresh()
    }

    fun noteText(reference: NoteReference): String =
        _data.days[reference.date]?.notes?.firstOrNull { it.id == reference.noteId }?.text
            ?: reference.text

    fun hasTasksOn(date: String): Boolean {
        if (_data.days[date]?.tasks?.isNotEmpty() == true) return true
        val today = Dates.today()
        return date == today && _data.days.values.any { day ->
            day.tasks.any { task -> !task.done && task.date < today }
        }
    }

    fun hasNotesOn(date: String): Boolean =
        _data.days[date]?.notes?.isNotEmpty() == true

    fun updateReminderEnabled(enabled: Boolean) {
        _data.settings.reminderEnabled = enabled
        reminderEnabled = enabled
        commit()
        ReminderScheduler.sync(getApplication(), enabled, reminderTime)
    }

    fun updateReminderTime(time: String) {
        _data.settings.reminderTime = time
        reminderTime = time
        commit()
        ReminderScheduler.sync(getApplication(), reminderEnabled, time)
    }

    fun updateAutoCarry(enabled: Boolean) {
        _data.settings.autoCarry = enabled
        autoCarry = enabled
        commit()
    }

    private fun syncSettingsState() {
        reminderEnabled = _data.settings.reminderEnabled
        reminderTime = _data.settings.reminderTime
        autoCarry = _data.settings.autoCarry
    }

    private fun commit() {
        refresh()
        viewModelScope.launch {
            repo.save(_data)
        }
    }

    private fun dayData(date: String): DayData =
        _data.days.getOrPut(date) { DayData() }

    private fun updateTask(id: String, transform: (TaskItem) -> TaskItem) {
        for (day in _data.days.values) {
            val index = day.tasks.indexOfFirst { it.id == id }
            if (index >= 0) {
                day.tasks[index] = transform(day.tasks[index])
                commit()
                return
            }
        }
    }

    private fun removeTask(id: String): TaskItem? {
        for (day in _data.days.values) {
            val index = day.tasks.indexOfFirst { it.id == id }
            if (index >= 0) return day.tasks.removeAt(index)
        }
        return null
    }

    private fun moveTask(id: String, date: String) {
        val task = removeTask(id) ?: return
        dayData(date).tasks.add(task.copy(date = date))
        commit()
    }

    private fun parseTags(raw: String): List<String> =
        raw.split(',', '，')
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .take(4)

    private fun resetTransient() {
        hasRecalledDraft = false
        editingNoteId = null
        pendingQuote = null
        noteDraft = ""
    }

    private fun refresh() {
        val today = Dates.today()
        val allTasks = _data.days.values.flatMap { it.tasks }
        val result = _data.days[selectedDate]?.tasks.orEmpty()
            .map { TaskRow(it) }
            .toMutableList()

        if (selectedDate == today && _data.settings.autoCarry) {
            result.addAll(
                allTasks
                    .filter { it.date < today && !it.done }
                    .map { TaskRow(it, Dates.daysBetween(it.date, today)) }
            )
        }

        rows = result.sortedWith(
            compareBy<TaskRow> { it.item.done }
                .thenByDescending { it.item.priority }
                .thenBy { it.item.createdAt }
        )

        carriedRows = rows.filter { !it.item.done && it.carriedDays > 0L }
        openRows = rows.filter { !it.item.done && it.carriedDays == 0L }
        doneRows = rows.filter { it.item.done }

        notes = _data.days[selectedDate]?.notes.orEmpty()
            .sortedByDescending { it.createdAt }
    }
}
