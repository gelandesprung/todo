package com.daytodo.android.data

import android.content.Context
import com.daytodo.android.model.AppData
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File

class Repository(private val context: Context) {
    private val file = File(context.filesDir, "daytodo-data.json")
    private val temp = File(context.filesDir, "daytodo-data.json.tmp")

    val json = Json {
        prettyPrint = true
        ignoreUnknownKeys = true
    }

    suspend fun load(): AppData = withContext(Dispatchers.IO) {
        if (!file.exists()) {
            AppData()
        } else {
            try {
                json.decodeFromString<AppData>(file.readText())
            } catch (_: Exception) {
                AppData()
            }
        }
    }

    suspend fun save(data: AppData) = withContext(Dispatchers.IO) {
        temp.writeText(json.encodeToString(data))
        if (file.exists()) file.delete()
        temp.renameTo(file)
    }

    suspend fun exportJson(): String = withContext(Dispatchers.IO) {
        if (file.exists()) file.readText() else json.encodeToString(AppData())
    }

    suspend fun importJson(text: String): AppData = withContext(Dispatchers.IO) {
        json.decodeFromString<AppData>(text)
    }

    fun dataFileInfo(): String = file.absolutePath
}
