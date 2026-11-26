package com.daytodo.android.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * 搜索页：整屏切换而不是覆盖层，键盘弹起时仍有足够高度。
 * 只展示结果，点条目回原日期再操作，沿用「历史只读、今天可编辑」。
 */
@Composable
fun SearchScreen(
    vm: MainViewModel,
    snackbarHost: @Composable () -> Unit,
    onBack: () -> Unit,
    onOpenHit: (SearchHit) -> Unit
) {
    BackHandler { onBack() }

    val focusRequester = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(Unit) {
        focusRequester.requestFocus()
        keyboard?.show()
    }

    Scaffold(
        containerColor = TodoColors.Bg,
        snackbarHost = snackbarHost,
        topBar = {
            Surface(color = TodoColors.Surface, border = BorderStroke(0.5.dp, TodoColors.Line)) {
                Column {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(start = 4.dp, end = 8.dp, top = 4.dp, bottom = 2.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        IconButton(onClick = onBack, modifier = Modifier.size(44.dp)) {
                            Icon(
                                Icons.AutoMirrored.Filled.ArrowBack,
                                contentDescription = "返回",
                                tint = TodoColors.Text2
                            )
                        }
                        TextField(
                            value = vm.searchText,
                            onValueChange = { vm.updateSearchText(it) },
                            modifier = Modifier
                                .weight(1f)
                                .focusRequester(focusRequester)
                                .height(52.dp),
                            placeholder = {
                                Text("搜索任务、标签、随记", fontSize = 14.sp, color = TodoColors.Text3)
                            },
                            singleLine = true,
                            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                            keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
                            colors = TextFieldDefaults.colors(
                                focusedContainerColor = TodoColors.Panel,
                                unfocusedContainerColor = TodoColors.Panel,
                                focusedIndicatorColor = Color.Transparent,
                                unfocusedIndicatorColor = Color.Transparent,
                                disabledIndicatorColor = Color.Transparent,
                                cursorColor = TodoColors.Accent,
                                focusedTextColor = TodoColors.Text1,
                                unfocusedTextColor = TodoColors.Text1
                            ),
                            shape = RoundedCornerShape(12.dp),
                            trailingIcon = if (vm.searchText.isEmpty()) null else {
                                {
                                    IconButton(
                                        onClick = { vm.updateSearchText("") },
                                        modifier = Modifier.size(36.dp)
                                    ) {
                                        Icon(
                                            Icons.Filled.Close,
                                            contentDescription = "清空搜索",
                                            tint = TodoColors.Text3,
                                            modifier = Modifier.size(18.dp)
                                        )
                                    }
                                }
                            }
                        )
                    }
                    FilterRow(vm)
                    HorizontalDivider(thickness = 0.5.dp, color = TodoColors.Line)
                }
            }
        }
    ) { padding ->
        Column(modifier = Modifier.fillMaxSize().padding(padding).imePadding()) {
            when {
                vm.showSearchGuide -> SearchGuide()
                vm.hasNoSearchHits -> NoHits(query = vm.searchText)
                else -> {
                    Text(
                        text = vm.searchSummaryText,
                        fontSize = 12.sp,
                        color = TodoColors.Text3,
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
                    )
                    LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(start = 12.dp, end = 12.dp, bottom = 24.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        vm.searchGroups.forEach { group ->
                            item(key = "date-${group.date}") { GroupHeader(group) }
                            items(group.hits, key = { "${it.isTask}-${it.id}" }) { hit ->
                                HitCard(
                                    hit = hit,
                                    tokens = vm.searchTokens,
                                    onClick = { onOpenHit(hit) }
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun FilterRow(vm: MainViewModel) {
    val options = listOf(
        SearchFilter.All to "全部",
        SearchFilter.Tasks to "任务",
        SearchFilter.Notes to "随记",
        SearchFilter.Open to "未完成"
    )
    Row(
        modifier = Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, bottom = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        options.forEach { (filter, label) ->
            val active = vm.searchFilter == filter
            Surface(
                shape = RoundedCornerShape(18.dp),
                color = if (active) TodoColors.AccentLight else TodoColors.Panel,
                border = BorderStroke(0.5.dp, if (active) TodoColors.Accent else TodoColors.Line),
                modifier = Modifier.clickable { vm.changeSearchFilter(filter) }
            ) {
                Text(
                    text = label,
                    fontSize = 12.5.sp,
                    fontWeight = if (active) FontWeight.SemiBold else FontWeight.Normal,
                    color = if (active) TodoColors.AccentDark else TodoColors.Text2,
                    modifier = Modifier.padding(horizontal = 14.dp, vertical = 6.dp)
                )
            }
        }
    }
}

@Composable
private fun GroupHeader(group: SearchGroup) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(start = 4.dp, top = 8.dp, bottom = 2.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = group.label,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = TodoColors.Text3
        )
        Spacer(Modifier.width(8.dp))
        Surface(color = TodoColors.Chip, shape = CircleShape) {
            Text(
                text = group.hits.size.toString(),
                fontSize = 11.sp,
                color = TodoColors.Text3,
                modifier = Modifier.padding(horizontal = 7.dp, vertical = 1.dp)
            )
        }
    }
}

@Composable
private fun HitCard(hit: SearchHit, tokens: List<String>, onClick: () -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth().clickable(onClick = onClick),
        shape = RoundedCornerShape(12.dp),
        color = TodoColors.Surface,
        border = BorderStroke(1.dp, TodoColors.Line)
    ) {
        Row(
            modifier = Modifier.padding(start = 12.dp, end = 12.dp, top = 10.dp, bottom = 10.dp)
        ) {
            Icon(
                imageVector = if (hit.isTask) Icons.Filled.CheckCircle else Icons.Filled.Description,
                contentDescription = null,
                tint = if (hit.isDone) TodoColors.Text3 else TodoColors.Accent,
                modifier = Modifier.size(18.dp).padding(top = 2.dp)
            )
            Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                HighlightText(
                    text = hit.text,
                    tokens = tokens,
                    color = if (hit.isDone) TodoColors.Text3 else TodoColors.Text1,
                    strike = hit.isDone
                )
                Spacer(Modifier.height(6.dp))
                Text(
                    text = hit.meta,
                    fontSize = 12.sp,
                    color = TodoColors.Text3,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }
        }
    }
}

/** 多词命中高亮：先合并重叠区间，避免嵌套 addStyle 把颜色盖掉。 */
@Composable
private fun HighlightText(text: String, tokens: List<String>, color: Color, strike: Boolean) {
    val annotated = remember(text, tokens, strike) {
        buildAnnotatedString {
            append(text)
            if (text.isEmpty()) return@buildAnnotatedString
            if (strike) addStyle(SpanStyle(textDecoration = TextDecoration.LineThrough), 0, text.length)

            val ranges = mutableListOf<Pair<Int, Int>>()
            tokens.forEach { token ->
                if (token.isEmpty()) return@forEach
                var from = 0
                while (from < text.length) {
                    val index = text.indexOf(token, from, ignoreCase = true)
                    if (index < 0) break
                    ranges.add(index to index + token.length)
                    from = index + token.length
                }
            }
            if (ranges.isEmpty()) return@buildAnnotatedString

            val merged = mutableListOf<Pair<Int, Int>>()
            ranges.sortedBy { it.first }.forEach { (start, end) ->
                val last = merged.lastOrNull()
                when {
                    last == null -> merged.add(start to end)
                    start > last.second -> merged.add(start to end)
                    end > last.second -> merged[merged.lastIndex] = last.first to end
                }
            }
            merged.forEach { (start, end) ->
                addStyle(
                    SpanStyle(
                        color = TodoColors.AccentDark,
                        fontWeight = FontWeight.SemiBold,
                        background = TodoColors.AccentLight
                    ),
                    start,
                    end
                )
            }
        }
    }
    Text(
        text = annotated,
        fontSize = 14.sp,
        lineHeight = 20.sp,
        color = color,
        maxLines = 3,
        overflow = TextOverflow.Ellipsis
    )
}

@Composable
private fun SearchGuide() {
    SearchPlaceholder(
        icon = Icons.Filled.Search,
        title = "搜索所有日期",
        hint = "输入任务标题、标签或随记内容\n空格分词可同时匹配多个词"
    )
}

@Composable
private fun NoHits(query: String) {
    SearchPlaceholder(
        icon = Icons.Filled.Search,
        title = "没有找到匹配的内容",
        hint = "「${query.trim()}」没有命中\n换个关键词试试"
    )
}

@Composable
private fun SearchPlaceholder(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    title: String,
    hint: String
) {
    Column(
        modifier = Modifier.fillMaxSize().padding(start = 32.dp, end = 32.dp, bottom = 80.dp),
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
            text = hint,
            fontSize = 13.sp,
            color = TodoColors.Text3,
            lineHeight = 20.sp,
            textAlign = TextAlign.Center
        )
    }
}
