package com.daytodo.android.ui

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

val Accent = Color(0xFF378ADD)
val AccentDark = Color(0xFF185FA5)
val AccentLight = Color(0xFFE6F1FB)
val TextPrimary = Color(0xFF2C2C2A)
val TextSecondary = Color(0xFF5F5E5A)
val TextMuted = Color(0xFFB4B2A9)
val Line = Color(0xFFEFEDE8)
val Panel = Color(0xFFFAFAF8)
val Chip = Color(0xFFF1EFE8)
val PriorityHigh = Color(0xFFE24B4A)
val PriorityNormal = Color(0xFFEF9F27)
val PriorityLow = Color(0xFFC9C7BE)

@Composable
fun DayTodoTheme(content: @Composable () -> Unit) {
    val colors = lightColorScheme(
        primary = Accent,
        onPrimary = Color.White,
        primaryContainer = AccentLight,
        onPrimaryContainer = AccentDark,
        secondary = TextSecondary,
        onSecondary = Color.White,
        background = Panel,
        onBackground = TextPrimary,
        surface = Color.White,
        onSurface = TextPrimary,
        surfaceVariant = Panel,
        onSurfaceVariant = TextSecondary,
        outline = Line,
        error = PriorityHigh
    )

    val typography = Typography(
        bodyMedium = TextStyle(fontSize = 13.sp, fontWeight = FontWeight.Normal, color = TextPrimary),
        titleMedium = TextStyle(fontSize = 15.sp, fontWeight = FontWeight.Medium, color = TextPrimary),
        labelLarge = TextStyle(fontSize = 13.sp, fontWeight = FontWeight.Medium)
    )

    val shapes = Shapes(
        small = RoundedCornerShape(6.dp),
        medium = RoundedCornerShape(8.dp),
        large = RoundedCornerShape(12.dp)
    )

    MaterialTheme(
        colorScheme = colors,
        typography = typography,
        shapes = shapes,
        content = content
    )
}
