@file:OptIn(ExperimentalMaterial3Api::class)

package com.daytodo.android.ui

import android.Manifest
import android.app.TimePickerDialog
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.runtime.rememberCoroutineScope
import kotlinx.coroutines.launch

@Composable
fun SettingsScreen(
    vm: MainViewModel,
    snackbarHost: @Composable () -> Unit,
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
                    onToast("已导出备份")
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
                    val text = context.contentResolver.openInputStream(uri)
                        ?.bufferedReader()
                        ?.use { it.readText() }
                    if (text != null) {
                        vm.importJson(text) { ok -> onToast(if (ok) "已导入备份" else "文件格式不对") }
                    }
                } catch (_: Exception) {
                    onToast("导入失败")
                }
            }
        }
    }

    Scaffold(
        containerColor = TodoColors.Bg,
        snackbarHost = snackbarHost,
        topBar = {
            TopAppBar(
                title = { Text("设置", style = MaterialTheme.typography.titleMedium) },
                navigationIcon = {
                    IconButton(onClick = onBack, modifier = Modifier.size(44.dp)) {
                        Icon(
                            Icons.AutoMirrored.Filled.ArrowBack,
                            contentDescription = "返回",
                            tint = TodoColors.Text2
                        )
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = TodoColors.Surface)
            )
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(start = 12.dp, end = 12.dp, top = 12.dp, bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            item {
                SettingCard("通用") {
                    SwitchRow(
                        title = "每日提醒",
                        hint = "每天 ${vm.reminderTime} 提醒查看清单",
                        checked = vm.reminderEnabled
                    ) { enabled ->
                        if (enabled && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                            permissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                        } else {
                            vm.updateReminderEnabled(enabled)
                        }
                    }
                    HorizontalDivider(color = TodoColors.Line)
                    TapRow(
                        title = "提醒时间",
                        hint = "点击修改",
                        value = vm.reminderTime
                    ) {
                        val parts = vm.reminderTime.split(":").mapNotNull { it.toIntOrNull() }
                        TimePickerDialog(
                            context,
                            { _, h, m -> vm.updateReminderTime("%02d:%02d".format(h, m)) },
                            parts.getOrNull(0) ?: 8,
                            parts.getOrNull(1) ?: 30,
                            true
                        ).show()
                    }
                    HorizontalDivider(color = TodoColors.Line)
                    SwitchRow(
                        title = "自动顺延未完成任务",
                        hint = "跨天时保留原日期记录",
                        checked = vm.autoCarry
                    ) { vm.updateAutoCarry(it) }
                }
            }

            item {
                SettingCard("数据") {
                    InfoRow("本地存储", vm.dataFileInfo())
                    HorizontalDivider(color = TodoColors.Line)
                    TapRow("导出备份", "生成一个 JSON 文件", null) {
                        exportLauncher.launch("daytodo-backup.json")
                    }
                    HorizontalDivider(color = TodoColors.Line)
                    TapRow("导入备份", "会覆盖当前所有数据", null) {
                        importLauncher.launch(arrayOf("application/json", "text/plain"))
                    }
                }
            }

            item {
                SettingCard("关于") {
                    Text(
                        "DayTodo Android 1.1",
                        fontSize = 15.sp,
                        color = TodoColors.Text1
                    )
                    Text(
                        "按天显示的轻量清单，未完成任务自动顺延到今天。桌面版与安卓版各自独立存储，互不同步。",
                        style = MaterialTheme.typography.bodySmall,
                        color = TodoColors.Text3,
                        modifier = Modifier.padding(top = 6.dp)
                    )
                }
            }
        }
    }
}

@Composable
private fun SettingCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        color = TodoColors.Surface,
        border = BorderStroke(1.dp, TodoColors.Line)
    ) {
        Column(Modifier.fillMaxWidth()) {
            Text(
                text = title,
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold,
                color = TodoColors.Text3,
                modifier = Modifier.padding(start = 16.dp, top = 14.dp, bottom = 6.dp)
            )
            content()
            Spacer(Modifier.height(4.dp))
        }
    }
}

@Composable
private fun SwitchRow(
    title: String,
    hint: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit
) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, fontSize = 15.sp, color = TodoColors.Text1)
            Text(
                text = hint,
                fontSize = 12.sp,
                color = TodoColors.Text3,
                modifier = Modifier.padding(top = 3.dp)
            )
        }
        Spacer(Modifier.width(12.dp))
        Switch(
            checked = checked,
            onCheckedChange = onChange,
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color.White,
                checkedTrackColor = TodoColors.Accent,
                uncheckedThumbColor = Color.White,
                uncheckedTrackColor = TodoColors.LineStrong
            )
        )
    }
}

@Composable
private fun TapRow(
    title: String,
    hint: String,
    value: String?,
    onClick: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(56.dp)
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, fontSize = 15.sp, color = TodoColors.Text1)
            Text(
                text = hint,
                fontSize = 12.sp,
                color = TodoColors.Text3,
                modifier = Modifier.padding(top = 2.dp)
            )
        }
        if (value != null) {
            Text(value, fontSize = 14.sp, color = TodoColors.AccentDark, fontWeight = FontWeight.Medium)
        } else {
            Icon(
                Icons.AutoMirrored.Filled.KeyboardArrowRight,
                contentDescription = null,
                tint = TodoColors.Text3,
                modifier = Modifier.size(20.dp)
            )
        }
    }
}

@Composable
private fun InfoRow(title: String, value: String) {
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp)) {
        Text(title, fontSize = 15.sp, color = TodoColors.Text1)
        Text(
            text = value,
            fontSize = 12.sp,
            color = TodoColors.Text3,
            modifier = Modifier.padding(top = 4.dp)
        )
    }
}
