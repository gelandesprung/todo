package com.daytodo.android.model

import kotlinx.serialization.Serializable
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.UUID

@Serializable
data class Preferences(
    var reminderEnabled: Boolean = false,
    var reminderTime: String = "08:30",
    var autoCarry: Boolean = true
)

@Serializable
data class TaskItem(
    val id: String,
    val date: String,
    val title: String,
    val tags: List<String> = emptyList(),
    val priority: Int = 1,
    val done: Boolean = false,
    val createdAt: Long = System.currentTimeMillis()
)

@Serializable
data class NoteReference(
    val date: String,
    val noteId: String,
    val text: String
)

@Serializable
data class NoteItem(
    val id: String,
    val date: String,
    val createdAt: Long,
    val text: String,
    val references: List<NoteReference> = emptyList()
)

@Serializable
data class DayData(
    val tasks: MutableList<TaskItem> = mutableListOf(),
    val notes: MutableList<NoteItem> = mutableListOf()
)

@Serializable
data class AppData(
    val days: MutableMap<String, DayData> = mutableMapOf(),
    var settings: Preferences = Preferences()
)

object Dates {
    private val iso: DateTimeFormatter = DateTimeFormatter.ISO_LOCAL_DATE

    fun today(): String = LocalDate.now().format(iso)

    fun parse(value: String): LocalDate = LocalDate.parse(value, iso)

    fun format(date: LocalDate): String = date.format(iso)

    fun shift(value: String, days: Long): String = format(parse(value).plusDays(days))

    fun daysBetween(from: String, to: String): Long =
        ChronoUnit.DAYS.between(parse(from), parse(to))

    fun label(value: String): String {
        val date = parse(value)
        val week = when (date.dayOfWeek.value) {
            1 -> "周一"
            2 -> "周二"
            3 -> "周三"
            4 -> "周四"
            5 -> "周五"
            6 -> "周六"
            else -> "周日"
        }
        return "${date.monthValue}月${date.dayOfMonth}日 $week"
    }

    fun relation(value: String): String = when {
        value == today() -> "今天"
        value > today() -> "计划"
        else -> "历史"
    }
}

fun newTaskId(): String = UUID.randomUUID().toString()

fun newNoteId(): String = UUID.randomUUID().toString()
