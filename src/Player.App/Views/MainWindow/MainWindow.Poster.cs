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
    /// 在后台加载 hero 图片，完成后只为当前仍被选中的条目替换图片。
    /// </summary>
    private async Task LoadPosterImageAsync(EmbyItem item, CancellationToken selectionToken)
    {
        CancelPosterLoad();
        var candidates = await _artworkResolver.ResolvePosterCandidatesAsync(
            item,
            _settings.UserId,
            _settings.AccessToken,
            selectionToken);
        if (selectionToken.IsCancellationRequested || !ReferenceEquals(_selectedItem, item))
        {
            return;
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var posterCancellation = new CancellationLease();
        _posterCancellation = posterCancellation;
        var linkedToken = posterCancellation.StartLinked(selectionToken);

        _ = Task.Run(
            () =>
            {
                foreach (var uri in candidates)
                {
                    try
                    {
                        var token = linkedToken;
                        token.ThrowIfCancellationRequested();

                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        image.UriSource = uri;
                        image.DecodePixelWidth = 1100;
                        image.EndInit();
                        image.Freeze();
                        token.ThrowIfCancellationRequested();
                        return image;
                    }
                    catch
                    {
                        // 当前候选图不可用时继续尝试下一个 URL，避免详情区只剩黑底。
                    }
                }

                return null;
            },
            posterCancellation.Token).ContinueWith(
                task =>
                {
                    if (posterCancellation.IsCancellationRequested ||
                        !ReferenceEquals(_posterCancellation, posterCancellation) ||
                        !ReferenceEquals(_selectedItem, item))
                    {
                        return;
                    }

                    if (task.Status == TaskStatus.RanToCompletion && task.Result is not null)
                    {
                        PosterImage.Source = task.Result;
                    }

                    CancelPosterLoad();
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// 让详情横幅只绑定当前条目的缩略图，避免切换时残留上一条目的封面。
    /// </summary>
    private void BindPosterImage(EmbyItem item)
    {
        BindingOperations.SetBinding(
            PosterImage,
            Image.SourceProperty,
            new Binding(nameof(EmbyItem.ThumbnailUri))
            {
                Source = item,
                Mode = BindingMode.OneWay
            });
    }
}


