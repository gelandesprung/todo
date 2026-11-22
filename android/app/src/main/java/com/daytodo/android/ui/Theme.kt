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

/**
 * 颜色 token 集中处。界面只从这里取色，不再出现散落的 Color(0xFF...)。
 * 以后接深色模式，只需在这里加一套 dark 映射，组件无需改动。
 */
object TodoColors {
    val Accent = Color(0xFF378ADD)
    val AccentDark = Color(0xFF185FA5)
    val AccentLight = Color(0xFFE6F1FB)

    val Bg = Color(0xFFF6F6F3)
    val Surface = Color(0xFFFFFFFF)
    val Panel = Color(0xFFFAFAF8)

    // 正文三级：对白底分别约 16.1:1 / 8.1:1 / 4.9:1，全部过 WCAG AA
    val Text1 = Color(0xFF1F1F1E)
    val Text2 = Color(0xFF55544F)
    val Text3 = Color(0xFF75736C)

    val Line = Color(0xFFECEAE4)
    val LineStrong = Color(0xFFDCD9D0)
    val Chip = Color(0xFFF2F0E9)

    // 优先级：色值对白底均 ≥5:1，同时配文字，不靠颜色单独传意
    val High = Color(0xFFC93B3B)
    val HighBg = Color(0xFFFCF0F0)
    val Mid = Color(0xFF9C5D00)
    val MidBg = Color(0xFFFBF2E4)
    val Low = Color(0xFF6E6C66)
    val LowBg = Color(0xFFF1F0EC)

    val Snack = Color(0xFF2B2B29)
    val SnackAction = Color(0xFF8FC4F5)
}

/**
 * 旧常量保留，避免遗漏引用时整片编译失败。
 */
val Accent = TodoColors.Accent
val AccentDark = TodoColors.AccentDark
val AccentLight = TodoColors.AccentLight
val TextPrimary = TodoColors.Text1
val TextSecondary = TodoColors.Text2
val TextMuted = TodoColors.Text3
val Text3 = TodoColors.Text3
val Line = TodoColors.Line
val Panel = TodoColors.Panel
val Chip = TodoColors.Chip
val PriorityHigh = TodoColors.High
val PriorityNormal = TodoColors.Mid
val PriorityLow = TodoColors.Low

/**
 * 字号刻度：24 / 17 / 15 / 14 / 13 / 12 / 11。
 * 11sp 只用于角标，正文不再低于 13sp。
 */
val TodoTypography = Typography(
    displaySmall = TextStyle(
        fontSize = 24.sp,
        lineHeight = 30.sp,
        fontWeight = FontWeight.SemiBold,
        letterSpacing = (-0.4).sp
    ),
    titleMedium = TextStyle(
        fontSize = 17.sp,
        lineHeight = 24.sp,
        fontWeight = FontWeight.SemiBold
    ),
    titleSmall = TextStyle(
        fontSize = 15.sp,
        lineHeight = 22.sp,
        fontWeight = FontWeight.Medium
    ),
    bodyLarge = TextStyle(
        fontSize = 15.sp,
        lineHeight = 22.sp
    ),
    bodyMedium = TextStyle(
        fontSize = 14.sp,
        lineHeight = 21.sp
    ),
    bodySmall = TextStyle(
        fontSize = 13.sp,
        lineHeight = 19.sp
    ),
    labelLarge = TextStyle(
        fontSize = 14.sp,
        fontWeight = FontWeight.Medium
    ),
    labelMedium = TextStyle(
        fontSize = 12.sp,
        lineHeight = 16.sp,
        fontWeight = FontWeight.Medium
    ),
    labelSmall = TextStyle(
        fontSize = 11.sp,
        lineHeight = 14.sp
    )
)

val TodoShapes = Shapes(
    small = RoundedCornerShape(8.dp),
    medium = RoundedCornerShape(12.dp),
    large = RoundedCornerShape(16.dp)
)

@Composable
fun DayTodoTheme(content: @Composable () -> Unit) {
    val colors = lightColorScheme(
        primary = TodoColors.Accent,
        onPrimary = Color.White,
        primaryContainer = TodoColors.AccentLight,
        onPrimaryContainer = TodoColors.AccentDark,
        secondary = TodoColors.Text2,
        onSecondary = Color.White,
        background = TodoColors.Bg,
        onBackground = TodoColors.Text1,
        surface = TodoColors.Surface,
        onSurface = TodoColors.Text1,
        surfaceVariant = TodoColors.Panel,
        onSurfaceVariant = TodoColors.Text2,
        outline = TodoColors.Line,
        outlineVariant = TodoColors.Line,
        error = TodoColors.High
    )

    MaterialTheme(
        colorScheme = colors,
        typography = TodoTypography,
        shapes = TodoShapes,
        content = content
    )
}
