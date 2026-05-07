using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Player.App.Models;

/// <summary>
/// Emby 账号密码认证成功后返回的响应。
/// </summary>
public sealed class AuthenticationResult
{
    public string AccessToken { get; set; } = "";
    public EmbyUser? User { get; set; }
}

/// <summary>
/// token 持久化和界面显示所需的最小用户信息。
/// </summary>
public sealed class EmbyUser
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// 大多数 Emby 媒体库接口返回的标准分页条目包装。
/// </summary>
public sealed class EmbyItemsResponse
{
    public int TotalRecordCount { get; set; }
    public int StartIndex { get; set; }
    public List<EmbyItem> Items { get; set; } = [];
}

/// <summary>
/// 投影到主列表和播放详情面板中的媒体库条目。
/// </summary>
public sealed class EmbyItem : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? ParentId { get; set; }
    public string? SeriesId { get; set; }
    public string? SeasonId { get; set; }
    public string? SeriesName { get; set; }
    public string? SeasonName { get; set; }
    public int? IndexNumber { get; set; }
    public int? ParentIndexNumber { get; set; }
    public int? ProductionYear { get; set; }
    public string? OfficialRating { get; set; }
    public string? Overview { get; set; }
    public Dictionary<string, string>? ImageTags { get; set; }
    public List<string>? BackdropImageTags { get; set; }
    public string? ParentBackdropItemId { get; set; }
    public List<string>? ParentBackdropImageTags { get; set; }
    public string? ParentPrimaryImageItemId { get; set; }
    public string? ParentPrimaryImageTag { get; set; }
    public string? ParentThumbItemId { get; set; }
    public string? ParentThumbImageItemId { get; set; }
    public string? ParentThumbImageTag { get; set; }
    public string? SeriesPrimaryImageTag { get; set; }
    public string? SeriesThumbImageTag { get; set; }
    private EmbyUserData? _userData;
    private Uri? _thumbnailUri;
    private long? _runTimeTicks;
    private DateTimeOffset? _datePlayed;

    /// <summary>
    /// 媒体总时长；播放结束刷新状态时变更它会影响进度百分比和元信息显示。
    /// </summary>
    public long? RunTimeTicks
    {
        get => _runTimeTicks;
        set
        {
            if (_runTimeTicks == value)
            {
                return;
            }

            _runTimeTicks = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetaLine));
            OnPropertyChanged(nameof(ResumeProgressPercent));
        }
    }

    /// <summary>
    /// 当前用户最后播放时间；mpv 停止后从 Emby 回读时需要通知列表刷新。
    /// </summary>
    public DateTimeOffset? DatePlayed
    {
        get => _datePlayed;
        set
        {
            if (_datePlayed == value)
            {
                return;
            }

            _datePlayed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetaLine));
        }
    }

    /// <summary>
    /// 服务器返回的用户播放状态；界面会根据它刷新续播进度和“已播放”徽记。
    /// </summary>
    public EmbyUserData? UserData
    {
        get => _userData;
        set
        {
            if (ReferenceEquals(_userData, value))
            {
                return;
            }

            if (_userData is not null)
            {
                _userData.PropertyChanged -= HandleUserDataPropertyChanged;
            }

            _userData = value;
            if (_userData is not null)
            {
                _userData.PropertyChanged += HandleUserDataPropertyChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlayed));
            OnPropertyChanged(nameof(MetaLine));
            OnPropertyChanged(nameof(ResumeProgressPercent));
        }
    }

    /// <summary>
    /// 界面卡片使用的缩略图地址，由主窗口在拿到服务器 token 后补齐。
    /// </summary>
    [JsonIgnore]
    public Uri? ThumbnailUri
    {
        get => _thumbnailUri;
        set
        {
            if (Equals(_thumbnailUri, value))
            {
                return;
            }

            _thumbnailUri = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    /// <summary>
    /// 列表缩略图真正加载失败后隐藏图片控件，避免空白图层覆盖深色占位。
    /// </summary>
    [JsonIgnore]
    public bool HasThumbnail => ThumbnailUri is not null;

    /// <summary>
    /// 面向用户的标题，避免在 XAML 中额外拼接剧集编号和电影年份。
    /// </summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (Type.Equals("Episode", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = "";
                if (ParentIndexNumber is not null && IndexNumber is not null)
                {
                    prefix = $"S{ParentIndexNumber:00}E{IndexNumber:00} ";
                }

                return string.IsNullOrWhiteSpace(SeriesName)
                    ? $"{prefix}{Name}"
                    : $"{SeriesName} - {prefix}{Name}";
            }

            return ProductionYear is null ? Name : $"{Name} ({ProductionYear})";
        }
    }

    /// <summary>
    /// 季选择器里显示的短标签，优先使用 Emby 的季号，避免下拉框内容过长。
    /// </summary>
    [JsonIgnore]
    public string SeasonSelectorLabel
    {
        get
        {
            if (IndexNumber is { } index && index > 0)
            {
                return $"第 {index} 季";
            }

            return string.IsNullOrWhiteSpace(Name) ? "未知季" : Name;
        }
    }

    /// <summary>
    /// 集选择器里显示的短标签，用集号和标题表达当前可播放条目。
    /// </summary>
    [JsonIgnore]
    public string EpisodeSelectorLabel
    {
        get
        {
            if (IndexNumber is { } index && index > 0)
            {
                return $"第 {index} 集  {Name}";
            }

            return string.IsNullOrWhiteSpace(Name) ? DisplayTitle : Name;
        }
    }

    /// <summary>
    /// 为 ComboBox 选中项和调试输出提供稳定文本，避免自定义模板退回显示 CLR 类型名。
    /// </summary>
    public override string ToString()
    {
        if (Type.Equals("Season", StringComparison.OrdinalIgnoreCase))
        {
            return SeasonSelectorLabel;
        }

        if (Type.Equals("Episode", StringComparison.OrdinalIgnoreCase))
        {
            return EpisodeSelectorLabel;
        }

        return DisplayTitle;
    }

    /// <summary>
    /// 续播之外的人工状态入口需要这个标记，便于在卡片上展示“已播放”徽记。
    /// </summary>
    [JsonIgnore]
    public bool IsPlayed => UserData?.Played == true;

    /// <summary>
    /// 列表行使用的紧凑元信息；当 Emby 返回用户数据时包含续播和播放历史。
    /// </summary>
    [JsonIgnore]
    public string MetaLine
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Type))
            {
                parts.Add(Type);
            }

            if (RunTimeTicks is { } ticks && ticks > 0)
            {
                parts.Add(TimeSpan.FromTicks(ticks).ToString(@"h\h\ mm\m"));
            }

            if (!string.IsNullOrWhiteSpace(OfficialRating))
            {
                parts.Add(OfficialRating);
            }

            if (DatePlayed is { } played)
            {
                parts.Add($"播放于 {played.LocalDateTime:yyyy-MM-dd HH:mm}");
            }

            if (UserData?.PlaybackPositionTicks is { } position && position > 0)
            {
                parts.Add($"续播 {TimeSpan.FromTicks(position):hh\\:mm\\:ss}");
            }

            return string.Join("  |  ", parts);
        }
    }

    /// <summary>
    /// 续播进度百分比，用于横向卡片底部的轻量进度条。
    /// </summary>
    [JsonIgnore]
    public double ResumeProgressPercent
    {
        get
        {
            if (RunTimeTicks is not { } runtime || runtime <= 0 ||
                UserData?.PlaybackPositionTicks is not { } position || position <= 0)
            {
                return UserData?.Played == true ? 100 : 0;
            }

            if (UserData?.Played == true)
            {
                return 100;
            }

            return Math.Clamp(position * 100.0 / runtime, 0, 100);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void HandleUserDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmbyUserData.Played) or nameof(EmbyUserData.PlaybackPositionTicks))
        {
            OnPropertyChanged(nameof(IsPlayed));
            OnPropertyChanged(nameof(MetaLine));
            OnPropertyChanged(nameof(ResumeProgressPercent));
        }
    }
}

/// <summary>
/// 每个用户的播放状态，用于续播和最近播放提示。
/// </summary>
public sealed class EmbyUserData : INotifyPropertyChanged
{
    private long _playbackPositionTicks;
    private bool _played;

    public long PlaybackPositionTicks
    {
        get => _playbackPositionTicks;
        set
        {
            if (_playbackPositionTicks == value)
            {
                return;
            }

            _playbackPositionTicks = value;
            OnPropertyChanged();
        }
    }

    public bool Played
    {
        get => _played;
        set
        {
            if (_played == value)
            {
                return;
            }

            _played = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Emby 播放协商响应，用于选择可播放媒体源。
/// </summary>
public sealed class PlaybackInfoResponse
{
    public List<MediaSource> MediaSources { get; set; } = [];
    public string? PlaySessionId { get; set; }
}

/// <summary>
/// Emby 为选中条目返回的具体串流候选。
/// </summary>
public sealed class MediaSource
{
    public string Id { get; set; } = "";
    public string? Container { get; set; }
    public string? Path { get; set; }
    public string? DirectStreamUrl { get; set; }
    public long? Size { get; set; }
    public long? Bitrate { get; set; }
    public string? Name { get; set; }
    public int? DefaultAudioStreamIndex { get; set; }
    public int? DefaultSubtitleStreamIndex { get; set; }
    public bool SupportsDirectStream { get; set; }
    public List<MediaStream> MediaStreams { get; set; } = [];

    /// <summary>
    /// 媒体源下拉框使用的短标签；优先展示版本名、容器和资源大小，避免路径占满界面。
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var container = string.IsNullOrWhiteSpace(Container) ? "stream" : Container;
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Name))
            {
                parts.Add(Name);
            }

            parts.Add(container.ToUpperInvariant());

            if (Size is { } size && size > 0)
            {
                parts.Add(FormatFileSize(size));
            }

            return string.Join("  |  ", parts);
        }
    }

    /// <summary>
    /// 详情区用于展示本版本资源大小、码率和路径摘要。
    /// </summary>
    [JsonIgnore]
    public string DetailLine
    {
        get
        {
            var parts = new List<string>();

            if (Size is { } size && size > 0)
            {
                parts.Add($"大小 {FormatFileSize(size)}");
            }

            if (Bitrate is { } bitrate && bitrate > 0)
            {
                parts.Add($"码率 {FormatBitrate(bitrate)}");
            }

            if (!string.IsNullOrWhiteSpace(Path))
            {
                parts.Add(Path);
            }

            return string.Join("  |  ", parts);
        }
    }

    /// <summary>
    /// 当前媒体源里的视频轨列表，供界面选择版本内的视频流。
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<MediaTrackOption> VideoTrackOptions =>
        BuildTrackOptions("Video", autoLabel: "视频自动", includeNoneOption: false, noneLabel: "");

    /// <summary>
    /// 当前媒体源里的音频轨列表，供界面选择语言或音轨版本。
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<MediaTrackOption> AudioTrackOptions =>
        BuildTrackOptions("Audio", autoLabel: "音频自动", includeNoneOption: false, noneLabel: "");

    /// <summary>
    /// 当前媒体源里的字幕轨列表；首项允许主动关闭字幕。
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<MediaTrackOption> SubtitleTrackOptions =>
        BuildTrackOptions("Subtitle", autoLabel: "字幕自动", includeNoneOption: true, noneLabel: "关闭字幕");

    /// <summary>
    /// 让自定义下拉框选中项直接显示用户可读标签，而不是类型名。
    /// </summary>
    public override string ToString() => DisplayName;

    private IReadOnlyList<MediaTrackOption> BuildTrackOptions(
        string type,
        string autoLabel,
        bool includeNoneOption,
        string noneLabel)
    {
        var options = MediaStreams
            .Where(stream => stream.Type?.Equals(type, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(stream => stream.Index ?? int.MaxValue)
            .Select(stream => MediaTrackOption.FromStream(stream))
            .ToList();

        if (options.Count > 0)
        {
            options.Insert(0, MediaTrackOption.Auto(autoLabel));
        }

        if (includeNoneOption)
        {
            options.Insert(options.Count > 0 ? 1 : 0, MediaTrackOption.None(noneLabel));
        }

        return options;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
    }

    private static string FormatBitrate(long bitrate)
    {
        return bitrate >= 1_000_000
            ? $"{bitrate / 1_000_000.0:0.##} Mbps"
            : $"{bitrate / 1_000.0:0.##} Kbps";
    }
}

/// <summary>
/// 媒体源携带的音频、视频或字幕流元数据。
/// </summary>
public sealed class MediaStream
{
    public int? Index { get; set; }
    public string? Type { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? DisplayTitle { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? BitRate { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsExternal { get; set; }
}

/// <summary>
/// 供界面下拉框选择的视频、音频或字幕轨道。
/// </summary>
public sealed class MediaTrackOption
{
    public int? Index { get; init; }
    public string DisplayName { get; init; } = "";
    public bool IsNone { get; init; }
    public bool IsAuto { get; init; }

    /// <summary>
    /// 从 Emby 媒体流生成 mpv 可用的轨道选项，保留 Emby 的流索引作为启动参数。
    /// </summary>
    public static MediaTrackOption FromStream(MediaStream stream)
    {
        var parts = new List<string>();

        if (stream.Index is { } index)
        {
            parts.Add($"#{index}");
        }

        if (!string.IsNullOrWhiteSpace(stream.DisplayTitle))
        {
            parts.Add(stream.DisplayTitle);
        }
        else if (!string.IsNullOrWhiteSpace(stream.Title))
        {
            parts.Add(stream.Title);
        }

        if (!string.IsNullOrWhiteSpace(stream.Language))
        {
            parts.Add(stream.Language);
        }

        if (!string.IsNullOrWhiteSpace(stream.Codec))
        {
            parts.Add(stream.Codec.ToUpperInvariant());
        }

        if (stream.IsDefault)
        {
            parts.Add("默认");
        }

        if (stream.IsForced)
        {
            parts.Add("强制");
        }

        if (stream.Width is { } width && stream.Height is { } height && width > 0 && height > 0)
        {
            parts.Add($"{width}x{height}");
        }

        if (stream.Channels is { } channels && channels > 0)
        {
            parts.Add($"{channels}ch");
        }
        else if (!string.IsNullOrWhiteSpace(stream.ChannelLayout))
        {
            parts.Add(stream.ChannelLayout);
        }

        if (stream.IsExternal)
        {
            parts.Add("外挂");
        }

        return new MediaTrackOption
        {
            Index = stream.Index,
            DisplayName = parts.Count == 0 ? "默认轨道" : string.Join("  |  ", parts)
        };
    }

    /// <summary>
    /// 生成禁用轨道的选项，目前主要用于字幕选择。
    /// </summary>
    public static MediaTrackOption None(string displayName)
    {
        return new MediaTrackOption
        {
            DisplayName = displayName,
            IsNone = true
        };
    }

    /// <summary>
    /// 生成“自动选择”选项；播放时不传 mpv 参数，让 mpv 使用自身默认轨道策略。
    /// </summary>
    public static MediaTrackOption Auto(string displayName)
    {
        return new MediaTrackOption
        {
            DisplayName = displayName,
            IsAuto = true
        };
    }

    public override string ToString() => DisplayName;
}
