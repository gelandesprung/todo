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

enum class SearchFilter {
    All,
    Tasks,
    Notes,
    Open
}

data class TaskRow(
    val item: TaskItem,
    val carriedDays: Long = 0
)

/** 搜索命中项：只读，点击只负责跳回原日期与标签。 */
data class SearchHit(
    val id: String,
    val date: String,
    val text: String,
    val isTask: Boolean,
    val isDone: Boolean,
    val meta: String
)

/** 按日期分组的搜索结果，界面直接渲染。 */
data class SearchGroup(
    val date: String,
    val label: String,
    val hits: List<SearchHit>
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

    // ---- 搜索：临时覆盖视图，不写数据、不改日历选中值 ----
    var isSearching by mutableStateOf(false)
        private set

    var searchText by mutableStateOf("")
        private set

    var searchFilter by mutableStateOf(SearchFilter.All)
        private set

    var searchGroups by mutableStateOf<List<SearchGroup>>(emptyList())
        private set

    val searchTokens: List<String>
        get() = if (searchText.isBlank()) emptyList()
                else searchText.split(Regex("\\s+")).filter { it.isNotEmpty() }

    val hasSearchQuery: Boolean get() = searchTokens.isNotEmpty()

    val searchHitCount: Int get() = searchGroups.sumOf { it.hits.size }

    val showSearchGuide: Boolean get() = !hasSearchQuery

    val hasNoSearchHits: Boolean get() = hasSearchQuery && searchHitCount == 0

    val searchSummaryText: String
        get() = if (!hasSearchQuery) "输入关键词，搜索全部日期"
                else "找到 $searchHitCount 条 · 按日期排序"

    /** 跳到日期条覆盖范围之外时临时补的胶囊；改选日期或退出搜索后清掉。 */
    var extraDate by mutableStateOf<String?>(null)
        private set

    /** 跳转目标行的短暂高亮，由界面延时清除。 */
    var highlightHitId by mutableStateOf<String?>(null)
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
        if (extraDate != null && extraDate != value) extraDate = null
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
        extraDate = null
        resetTransient()
        refresh()
    }

    fun openSearch() {
        if (isSearching) return
        isSearching = true
        rebuildSearch()
    }

    fun closeSearch() {
        if (!isSearching) return
        isSearching = false
        searchText = ""
        searchFilter = SearchFilter.All
        extraDate = null
        rebuildSearch()
    }

    fun updateSearchText(value: String) {
        searchText = value
        rebuildSearch()
    }

    fun changeSearchFilter(value: SearchFilter) {
        if (searchFilter == value) return
        searchFilter = value
        rebuildSearch()
    }

    fun clearHighlight() {
        highlightHitId = null
    }

    /** 结果只读：退出搜索、切到命中日期与标签、滚动定位由界面完成，这里只发信号。 */
    fun openSearchHit(hit: SearchHit) {
        val stillThere = if (hit.isTask) {
            _data.days.values.any { day -> day.tasks.any { it.id == hit.id } }
        } else {
            _data.days.values.any { day -> day.notes.any { it.id == hit.id } }
        }
        if (!stillThere) return

        closeSearch()
        tab = if (hit.isTask) AppTab.Tasks else AppTab.Notes
        selectedDate = hit.date
        val today = Dates.today()
        extraDate = if (hit.date < Dates.shift(today, -6) || hit.date > Dates.shift(today, 7)) hit.date else null
        resetTransient()
        refresh()
        highlightHitId = hit.id
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

    private fun rebuildSearch() {
        val tokens = searchTokens
        if (!isSearching || tokens.isEmpty()) {
            searchGroups = emptyList()
            return
        }
        val today = Dates.today()
        val hits = mutableListOf<SearchHit>()

        if (searchFilter != SearchFilter.Notes) {
            _data.days.values.flatMap { it.tasks }
                .filter { task ->
                    if (searchFilter == SearchFilter.Open && task.done) return@filter false
                    val haystack = task.title + " " + task.tags.joinToString(" ")
                    tokens.all { haystack.contains(it, ignoreCase = true) }
                }
                .forEach { task ->
                    val tags = if (task.tags.isEmpty()) "" else " · " + task.tags.joinToString(" ")
                    val priority = when (task.priority) {
                        2 -> "高"
                        1 -> "中"
                        else -> "低"
                    }
                    hits += SearchHit(
                        id = task.id,
                        date = effectiveTaskDate(task, today),
                        text = task.title,
                        isTask = true,
                        isDone = task.done,
                        meta = "任务 · $priority$tags · " + (if (task.done) "已完成" else "未完成")
                    )
                }
        }

        if (searchFilter == SearchFilter.All || searchFilter == SearchFilter.Notes) {
            _data.days.values.flatMap { it.notes }
                .filter { note -> tokens.all { token -> note.text.contains(token, ignoreCase = true) } }
                .forEach { note ->
                    hits += SearchHit(
                        id = note.id,
                        date = note.date,
                        text = note.text,
                        isTask = false,
                        isDone = false,
                        meta = "随记 · " + timeLabel(note.createdAt)
                    )
                }
        }

        searchGroups = hits.groupBy { it.date }
            .toSortedMap(compareByDescending<String> { it })
            .map { (date, dayHits) ->
                val label = when {
                    date == today -> "今天 · ${Dates.label(date)}"
                    date == Dates.shift(today, -1) -> "昨天 · ${Dates.label(date)}"
                    else -> Dates.label(date)
                }
                SearchGroup(
                    date = date,
                    label = label,
                    hits = dayHits.sortedWith(
                        compareByDescending<SearchHit> { it.isTask }.thenBy { it.text }
                    )
                )
            }
    }

    /** 顺延中的任务按「当前显示日」归属，跳过去才看得到。 */
    private fun effectiveTaskDate(task: TaskItem, today: String): String =
        if (_data.settings.autoCarry && !task.done && task.date < today) today else task.date

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
