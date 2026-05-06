using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Player.App.Models;
using Player.App.Services;

namespace Player.App;

public partial class MainWindow
{
    private async Task SearchAsync()
    {
        var term = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            await NavigateToAsync(CreateDefaultBrowseState());
            return;
        }

        await NavigateToAsync(new BrowseState(BrowseViewKind.Search, $"Search: {term}", null, term));
    }

    private async Task OpenContainerAsync(EmbyItem item)
    {
        await NavigateToAsync(new BrowseState(BrowseViewKind.Library, item.DisplayTitle, item.Id));
    }

    /// <summary>
    /// 继续观看页只承担入口职责；点击条目后进入详情态，避免入口页上方长期保留空白详情区。
    /// </summary>
    private async Task OpenResumeItemDetailAsync(EmbyItem item)
    {
        if (EmbyItemKind.IsContainer(item))
        {
            await OpenContainerAsync(item);
            return;
        }

        _navigationStack.Push(_currentState);
        _currentState = new BrowseState(BrowseViewKind.Detail, item.DisplayTitle, SelectedItemId: item.Id);
        ListTitleTextBlock.Text = "媒体详情";
        UpdateBackButtonVisibility();
        UpdateViewButtons(_currentState.Kind);
        SetDetailPanelVisibility(true);
        SetItemsListVisibility(false);
        await SelectItemAsync(item);
    }

    private async Task NavigateToAsync(BrowseState state)
    {
        // 详情态只是从列表条目点进去的临时页面，不对应一组可重新加载的列表数据；
        // 导航到搜索或继续观看时不把它压栈，避免返回时尝试加载不存在的详情列表。
        var pushCurrent = !_currentState.Equals(state) && _currentState.Kind != BrowseViewKind.Detail;
        await LoadViewAsync(state, pushCurrent);
    }

    /// <summary>
    /// 刷新当前视图，不改变返回栈。
    /// </summary>
    private Task ReloadCurrentViewAsync()
    {
        return LoadViewAsync(_currentState, pushCurrent: false);
    }

}


