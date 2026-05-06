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
    /// 构造当前选中条目的播放请求，启动 mpv.net，并建立后续连播链路。
    /// </summary>
    private async Task PlaySelectedAsync()
    {
        var item = _selectedItem;
        if (item is null)
        {
            SetStatus("请先选择一个条目。");
            return;
        }

        if (EmbyItemKind.IsContainer(item))
        {
            await OpenContainerAsync(item);
            return;
        }

        if (_selectedPlaybackInfo is null)
        {
            await SelectItemAsync(item);
        }

        var mediaSource = MediaSourceComboBox.SelectedItem as MediaSource
                          ?? _selectedPlaybackInfo?.MediaSources.FirstOrDefault();

        if (mediaSource is null)
        {
            SetStatus("没有找到可播放的媒体源。");
            return;
        }

        await RunGuardedAsync(
            "play media",
            async cancellationToken =>
            {
                _settings.MpvNetPath = MpvPathTextBox.Text.Trim();
                await SaveSettingsAsync();

                var request = PlaybackRequestFactory.CreateManualRequest(
                    item,
                    mediaSource,
                    _selectedPlaybackInfo,
                    ResumeCheckBox.IsChecked == true,
                    VideoTrackComboBox.SelectedItem as MediaTrackOption,
                    AudioTrackComboBox.SelectedItem as MediaTrackOption,
                    SubtitleTrackComboBox.SelectedItem as MediaTrackOption);
                var context = new PlaybackSessionContext(
                    _settings.UserId,
                    _settings.DeviceId,
                    _settings.AccessToken,
                    _settings.MpvNetPath);

                await _playbackCoordinator.StartAsync(
                    request,
                    context,
                    allowAutoPlayNext: AutoPlayCheckBox.IsChecked == true,
                    PrepareNextAutoPlaybackItemAsync,
                    ApplyAutoPlaybackItem,
                    cancellationToken);
            });
    }

}


