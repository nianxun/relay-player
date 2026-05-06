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
    /// <summary>
    /// 设置默认媒体源并同步轨道下拉，避免 SelectionChanged 在初始化期间重复刷新。
    /// </summary>
    private void ApplyInitialMediaSourceSelection(IReadOnlyList<MediaSource> mediaSources)
    {
        _isApplyingMediaSourceSelection = true;
        try
        {
            MediaSourceComboBox.SelectedIndex = mediaSources.Count > 0 ? 0 : -1;
        }
        finally
        {
            _isApplyingMediaSourceSelection = false;
        }

        UpdateMediaSourceDetails(MediaSourceComboBox.SelectedItem as MediaSource);
    }

    /// <summary>
    /// 根据当前媒体源刷新资源大小、视频轨、音频轨和字幕轨选择器。
    /// </summary>
    private void UpdateMediaSourceDetails(MediaSource? mediaSource)
    {
        if (mediaSource is null)
        {
            SelectedSourceTextBlock.Text = "";
            ClearTrackSelectors();
            return;
        }

        SelectedSourceTextBlock.Text = mediaSource.DetailLine;
        SetTrackSelector(VideoTrackComboBox, mediaSource.VideoTrackOptions);
        SetTrackSelector(AudioTrackComboBox, mediaSource.AudioTrackOptions);
        SetTrackSelector(SubtitleTrackComboBox, mediaSource.SubtitleTrackOptions);
    }

    /// <summary>
    /// 给轨道下拉框绑定选项，并默认选中第一个可用项。
    /// </summary>
    private static void SetTrackSelector(
        System.Windows.Controls.ComboBox comboBox,
        IReadOnlyList<MediaTrackOption> options)
    {
        comboBox.ItemsSource = options;
        comboBox.SelectedIndex = options.Count > 0 ? 0 : -1;
        comboBox.Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 清空轨道下拉框，避免切换条目后残留上一集的音轨或字幕。
    /// </summary>
    private void ClearTrackSelectors()
    {
        VideoTrackComboBox.ItemsSource = null;
        AudioTrackComboBox.ItemsSource = null;
        SubtitleTrackComboBox.ItemsSource = null;
        VideoTrackComboBox.Visibility = Visibility.Collapsed;
        AudioTrackComboBox.Visibility = Visibility.Collapsed;
        SubtitleTrackComboBox.Visibility = Visibility.Collapsed;
    }
}


