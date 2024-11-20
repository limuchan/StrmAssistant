using Emby.Media.Common.Extensions;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using StrmAssistant.Mod;
using StrmAssistant.Properties;
using StrmAssistant.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static StrmAssistant.ModOptions;

namespace StrmAssistant
{
    public class Plugin: BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        public static Plugin Instance { get; private set; }
        public static LibraryApi LibraryApi { get; private set; }
        public static ChapterApi ChapterApi { get; private set; }
        public static NotificationApi NotificationApi { get; private set; }
        public static SubtitleApi SubtitleApi { get; private set; }
        public static PlaySessionMonitor PlaySessionMonitor { get; private set; }
        public static MetadataApi MetadataApi { get; private set; }

        private readonly Guid _id = new Guid("63c322b7-a371-41a3-b11f-04f8418b37d8");

        public readonly ILogger logger;
        public readonly IApplicationHost ApplicationHost;
        public readonly IApplicationPaths ApplicationPaths;

        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        private bool _currentSuppressOnOptionsSaved;
        private int _currentMaxConcurrentCount;
        private bool _currentEnableImageCapture;
        private bool _currentCatchupMode;
        private bool _currentEnableIntroSkip;
        private bool _currentUnlockIntroSkip;
        private bool _currentMergeMultiVersion;
        private bool _currentChineseMovieDb;
        private bool _currentEnhanceMovieDbPerson;
        private bool _currentAltMovieDbConfig;
        private bool _currentAltMovieDbImageUrlEnabled;
        private bool _currentExclusiveExtract;
        private bool _currentPreferOriginalPoster;
        private bool _currentEnhanceChineseSearch;
        private string _currentSearchScope;
        private bool _currentPinyinSortName;
        private bool _currentEnhanceNfoMetadata;
        private bool _currentHidePersonNoImage;
        private bool _currentEnforceLibraryOrder;
        private bool _currentBeautifyMissingMetadata;

        public Plugin(IApplicationHost applicationHost,
            IApplicationPaths applicationPaths,
            ILogManager logManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            IItemRepository itemRepository,
            INotificationManager notificationManager,
            IMediaSourceManager mediaSourceManager,
            IMediaMountManager mediaMountManager,
            IMediaProbeManager mediaProbeManager,
            ILocalizationManager localizationManager,
            IUserManager userManager,
            IUserDataManager userDataManager,
            IFfmpegManager ffmpegManager,
            IMediaEncoder mediaEncoder,
            IJsonSerializer jsonSerializer,
            IServerApplicationHost serverApplicationHost,
            IServerConfigurationManager configurationManager) : base(applicationHost)
        {
            Instance = this;
            logger = logManager.GetLogger(Name);
            logger.Info("Plugin is getting loaded.");
            ApplicationHost = applicationHost;
            ApplicationPaths = applicationPaths;

            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;

            _currentMaxConcurrentCount = GetOptions().GeneralOptions.MaxConcurrentCount;
            _currentEnableImageCapture = GetOptions().MediaInfoExtractOptions.EnableImageCapture;
            _currentCatchupMode = GetOptions().GeneralOptions.CatchupMode;
            _currentEnableIntroSkip = GetOptions().IntroSkipOptions.EnableIntroSkip;
            _currentUnlockIntroSkip = GetOptions().IntroSkipOptions.UnlockIntroSkip;
            _currentMergeMultiVersion = GetOptions().ModOptions.MergeMultiVersion;
            _currentChineseMovieDb = GetOptions().MetadataEnhanceOptions.ChineseMovieDb;
            _currentEnhanceMovieDbPerson = GetOptions().MetadataEnhanceOptions.EnhanceMovieDbPerson;
            _currentAltMovieDbConfig = GetOptions().MetadataEnhanceOptions.AltMovieDbConfig;
            _currentAltMovieDbImageUrlEnabled =
                !string.IsNullOrEmpty(GetOptions().MetadataEnhanceOptions.AltMovieDbImageUrl);
            _currentExclusiveExtract = GetOptions().MediaInfoExtractOptions.ExclusiveExtract;
            _currentPreferOriginalPoster = GetOptions().MetadataEnhanceOptions.PreferOriginalPoster;
            _currentEnhanceChineseSearch = GetOptions().ModOptions.EnhanceChineseSearch;
            _currentSearchScope = GetOptions().ModOptions.SearchScope;
            _currentPinyinSortName = GetOptions().MetadataEnhanceOptions.PinyinSortName;
            _currentEnhanceNfoMetadata = GetOptions().MetadataEnhanceOptions.EnhanceNfoMetadata;
            _currentHidePersonNoImage = GetOptions().UIFunctionOptions.HidePersonNoImage;
            _currentEnforceLibraryOrder = GetOptions().UIFunctionOptions.EnforceLibraryOrder;
            _currentBeautifyMissingMetadata = GetOptions().UIFunctionOptions.BeautifyMissingMetadata;

            LibraryApi = new LibraryApi(libraryManager, fileSystem, mediaSourceManager, mediaMountManager, userManager);
            ChapterApi = new ChapterApi(libraryManager, itemRepository, fileSystem, applicationPaths, ffmpegManager,
                mediaEncoder, mediaMountManager, jsonSerializer, serverApplicationHost);
            PlaySessionMonitor = new PlaySessionMonitor(libraryManager, userManager, sessionManager);
            NotificationApi = new NotificationApi(notificationManager, userManager, sessionManager);
            SubtitleApi = new SubtitleApi(libraryManager, fileSystem, mediaProbeManager, localizationManager,
                itemRepository);
            MetadataApi = new MetadataApi(libraryManager, fileSystem, configurationManager, localizationManager);
            ShortcutMenuHelper.Initialize(configurationManager);

            PatchManager.Initialize();
            if (_currentCatchupMode) InitializeCatchupMode();
            if (_currentEnableIntroSkip) PlaySessionMonitor.Initialize();
            QueueManager.Initialize();

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;
            _userManager.UserCreated += OnUserCreated;
            _userManager.UserDeleted += OnUserDeleted;
            _userManager.UserConfigurationUpdated += OnUserConfigurationUpdated;
        }

        private void InitializeCatchupMode()
        {
            DisposeCatchupMode();
            _userDataManager.UserDataSaved += OnUserDataSaved;
        }

        private void DisposeCatchupMode()
        {
            _userDataManager.UserDataSaved -= OnUserDataSaved;
        }

        private void OnUserCreated(object sender, GenericEventArgs<User> e)
        {
            LibraryApi.FetchUsers();
        }

        private void OnUserDeleted(object sender, GenericEventArgs<User> e)
        {
            LibraryApi.FetchUsers();
        }
        
        private void OnUserConfigurationUpdated(object sender, GenericEventArgs<User> e)
        {
            if (e.Argument.Policy.IsAdministrator) LibraryApi.FetchAdminOrderedViews();
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (_currentCatchupMode && (_currentExclusiveExtract || e.Item.IsShortcut))
            {
                QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
            }

            if (_currentEnableIntroSkip && PlaySessionMonitor.IsLibraryInScope(e.Item))
            {
                if (!LibraryApi.HasMediaStream(e.Item))
                {
                    QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
                }
                else if (e.Item is Episode episode && ChapterApi.SeasonHasIntroCredits(episode))
                {
                    QueueManager.IntroSkipItemQueue.Enqueue(episode);
                }
            }

            NotificationApi.FavoritesUpdateSendNotification(e.Item);
        }

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (_currentEnhanceMovieDbPerson &&
                (e.UpdateReason & (ItemUpdateType.MetadataDownload | ItemUpdateType.MetadataImport)) != 0)
            {
                if (e.Item is Season season && season.IndexNumber > 0)
                {
                    LibraryApi.UpdateSeriesPeople(season.Parent as Series);
                }
                else if (e.Item is Series series)
                {
                    LibraryApi.UpdateSeriesPeople(series);
                }
            }
        }

        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (e.UserData.IsFavorite)
            {
                QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
            }
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public override string Description => "Extract MediaInfo and Enable IntroSkip";

        public override Guid Id => _id;

        public sealed override string Name => "Strm Assistant";

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Properties.thumb.png");
        }

        public PluginOptions GetPluginOptions()
        {
            return GetOptions();
        }

        public void SavePluginOptionsSuppress()
        {
            _currentSuppressOnOptionsSaved = true;
            SaveOptions(GetOptions());
        }

        protected override bool OnOptionsSaving(PluginOptions options)
        {
            var metadataEnhanceOptions = options.MetadataEnhanceOptions;

            metadataEnhanceOptions.AltMovieDbApiUrl =
                !string.IsNullOrWhiteSpace(metadataEnhanceOptions.AltMovieDbApiUrl)
                    ? metadataEnhanceOptions.AltMovieDbApiUrl.Trim().TrimEnd('/')
                    : metadataEnhanceOptions.AltMovieDbApiUrl?.Trim();

            metadataEnhanceOptions.AltMovieDbImageUrl =
                !string.IsNullOrWhiteSpace(metadataEnhanceOptions.AltMovieDbImageUrl)
                    ? metadataEnhanceOptions.AltMovieDbImageUrl.Trim().TrimEnd('/')
                    : metadataEnhanceOptions.AltMovieDbImageUrl?.Trim();

            return base.OnOptionsSaving(options);
        }

        protected override void OnOptionsSaved(PluginOptions options)
        {
            var suppressLogger = _currentSuppressOnOptionsSaved;

            if (!suppressLogger)
            {
                logger.Info("StrmOnly is set to {0}", options.GeneralOptions.StrmOnly);
                logger.Info("IncludeExtra is set to {0}", options.MediaInfoExtractOptions.IncludeExtra);
                logger.Info("MaxConcurrentCount is set to {0}", options.GeneralOptions.MaxConcurrentCount);
                var libraryScope = string.Join(", ",
                    options.MediaInfoExtractOptions.LibraryScope
                        ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v =>
                            options.MediaInfoExtractOptions.LibraryList.FirstOrDefault(option => option.Value == v)
                                ?.Name) ?? Enumerable.Empty<string>());
                logger.Info("MediaInfoExtract - LibraryScope is set to {0}",
                    string.IsNullOrEmpty(libraryScope) ? "ALL" : libraryScope);
            }
            if (_currentMaxConcurrentCount != options.GeneralOptions.MaxConcurrentCount)
            {
                _currentMaxConcurrentCount = options.GeneralOptions.MaxConcurrentCount;

                QueueManager.UpdateSemaphore(_currentMaxConcurrentCount);

                if (options.MediaInfoExtractOptions.EnableImageCapture)
                    EnableImageCapture.UpdateResourcePool(_currentMaxConcurrentCount);
            }

            if (!suppressLogger)
                logger.Info("EnableImageCapture is set to {0}", options.MediaInfoExtractOptions.EnableImageCapture);
            if (_currentEnableImageCapture != options.MediaInfoExtractOptions.EnableImageCapture)
            {
                _currentEnableImageCapture = options.MediaInfoExtractOptions.EnableImageCapture;

                if (_currentEnableImageCapture)
                {
                    EnableImageCapture.Patch();
                    if (_currentMaxConcurrentCount > 1) ApplicationHost.NotifyPendingRestart();
                }
                else
                {
                    EnableImageCapture.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("MergeMultiVersion is set to {0}", options.ModOptions.MergeMultiVersion);
            if (_currentMergeMultiVersion!= GetOptions().ModOptions.MergeMultiVersion)
            {
                _currentMergeMultiVersion = GetOptions().ModOptions.MergeMultiVersion;

                if (_currentMergeMultiVersion)
                {
                    MergeMultiVersion.Patch();
                }
                else
                {
                    MergeMultiVersion.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("ExclusiveExtract is set to {0}", options.MediaInfoExtractOptions.ExclusiveExtract);
            if (_currentExclusiveExtract != GetOptions().MediaInfoExtractOptions.ExclusiveExtract)
            {
                _currentExclusiveExtract = GetOptions().MediaInfoExtractOptions.ExclusiveExtract;

                if (_currentExclusiveExtract)
                {
                    ExclusiveExtract.Patch();
                }
                else
                {
                    ExclusiveExtract.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("ChineseMovieDb is set to {0}", options.MetadataEnhanceOptions.ChineseMovieDb);
            if (_currentChineseMovieDb != GetOptions().MetadataEnhanceOptions.ChineseMovieDb)
            {
                _currentChineseMovieDb = GetOptions().MetadataEnhanceOptions.ChineseMovieDb;

                if (_currentChineseMovieDb)
                {
                    ChineseMovieDb.Patch();
                }
                else
                {
                    ChineseMovieDb.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("EnhanceMovieDbPerson is set to {0}", options.MetadataEnhanceOptions.EnhanceMovieDbPerson);
            if (_currentEnhanceMovieDbPerson != GetOptions().MetadataEnhanceOptions.EnhanceMovieDbPerson)
            {
                _currentEnhanceMovieDbPerson = GetOptions().MetadataEnhanceOptions.EnhanceMovieDbPerson;

                if (_currentEnhanceMovieDbPerson)
                {
                    EnhanceMovieDbPerson.Patch();
                }
                else
                {
                    EnhanceMovieDbPerson.Unpatch();
                }
            }

            if (!suppressLogger)
            {
                logger.Info("AltMovieDbConfig is set to {0}", options.MetadataEnhanceOptions.AltMovieDbConfig);
                logger.Info("AltMovieDbApiUrl is set to {0}",
                    !string.IsNullOrEmpty(options.MetadataEnhanceOptions.AltMovieDbApiUrl)
                        ? options.MetadataEnhanceOptions.AltMovieDbApiUrl
                        : "NONE");
                logger.Info("AltMovieDbImageUrl is set to {0}",
                    !string.IsNullOrEmpty(options.MetadataEnhanceOptions.AltMovieDbImageUrl)
                        ? options.MetadataEnhanceOptions.AltMovieDbImageUrl
                        : "NONE");
                logger.Info("AltMovieDbApiKey is set to {0}",
                    !string.IsNullOrEmpty(options.MetadataEnhanceOptions.AltMovieDbApiKey)
                        ? options.MetadataEnhanceOptions.AltMovieDbApiKey
                        : "NONE");
            }

            if (_currentAltMovieDbConfig != GetOptions().MetadataEnhanceOptions.AltMovieDbConfig)
            {
                _currentAltMovieDbConfig = GetOptions().MetadataEnhanceOptions.AltMovieDbConfig;

                if (_currentAltMovieDbConfig)
                {
                    AltMovieDbConfig.PatchApiUrl();
                    if (_currentAltMovieDbImageUrlEnabled) AltMovieDbConfig.PatchImageUrl();
                }
                else
                {
                    AltMovieDbConfig.UnpatchApiUrl();
                    if (_currentAltMovieDbImageUrlEnabled) AltMovieDbConfig.UnpatchImageUrl();
                }
            }

            if (_currentAltMovieDbImageUrlEnabled !=
                !string.IsNullOrEmpty(GetOptions().MetadataEnhanceOptions.AltMovieDbImageUrl))
            {
                _currentAltMovieDbImageUrlEnabled =
                    !string.IsNullOrEmpty(GetOptions().MetadataEnhanceOptions.AltMovieDbImageUrl);

                if (_currentAltMovieDbImageUrlEnabled)
                {
                    AltMovieDbConfig.PatchImageUrl();
                }
                else
                {
                    AltMovieDbConfig.UnpatchImageUrl();
                }
            }

            if (!suppressLogger)
                logger.Info("PreferOriginalPoster is set to {0}", options.MetadataEnhanceOptions.PreferOriginalPoster);
            if (_currentPreferOriginalPoster != GetOptions().MetadataEnhanceOptions.PreferOriginalPoster)
            {
                _currentPreferOriginalPoster = GetOptions().MetadataEnhanceOptions.PreferOriginalPoster;

                if (_currentPreferOriginalPoster)
                {
                    PreferOriginalPoster.Patch();
                }
                else
                {
                    PreferOriginalPoster.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("EnhanceChineseSearch is set to {0}", options.ModOptions.EnhanceChineseSearch);
            if (_currentEnhanceChineseSearch != GetOptions().ModOptions.EnhanceChineseSearch)
            {
                _currentEnhanceChineseSearch = GetOptions().ModOptions.EnhanceChineseSearch;

                var isSimpleTokenizer = string.Equals(EnhanceChineseSearch.CurrentTokenizerName, "simple",
                    StringComparison.Ordinal);

                GetOptions().ModOptions.EnhanceChineseSearchRestore =
                    !_currentEnhanceChineseSearch && isSimpleTokenizer;
                SavePluginOptionsSuppress();

                if ((!_currentEnhanceChineseSearch && isSimpleTokenizer) ||
                    (_currentEnhanceChineseSearch && !isSimpleTokenizer))
                {
                    ApplicationHost.NotifyPendingRestart();
                }
            }

            if (!suppressLogger)
                logger.Info("PinyinSortName is set to {0}", options.MetadataEnhanceOptions.PinyinSortName);
            if (_currentPinyinSortName != GetOptions().MetadataEnhanceOptions.PinyinSortName)
            {
                _currentPinyinSortName = GetOptions().MetadataEnhanceOptions.PinyinSortName;

                if (_currentPinyinSortName)
                {
                    PinyinSortName.Patch();
                }
                else
                {
                    PinyinSortName.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("EnhanceNfoMetadata is set to {0}", options.MetadataEnhanceOptions.EnhanceNfoMetadata);
            if (_currentEnhanceNfoMetadata != GetOptions().MetadataEnhanceOptions.EnhanceNfoMetadata)
            {
                _currentEnhanceNfoMetadata = GetOptions().MetadataEnhanceOptions.EnhanceNfoMetadata;

                if (_currentEnhanceNfoMetadata)
                {
                    EnhanceNfoMetadata.Patch();
                }
                else
                {
                    EnhanceNfoMetadata.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("HidePersonNoImage is set to {0}", options.UIFunctionOptions.HidePersonNoImage);
            if (_currentHidePersonNoImage != GetOptions().UIFunctionOptions.HidePersonNoImage)
            {
                _currentHidePersonNoImage = GetOptions().UIFunctionOptions.HidePersonNoImage;

                if (_currentHidePersonNoImage)
                {
                    HidePersonNoImage.Patch();
                }
                else
                {
                    HidePersonNoImage.Unpatch();
                }
            }
            
            if (!suppressLogger)
                logger.Info("EnforceLibraryOrder is set to {0}", options.UIFunctionOptions.EnforceLibraryOrder);
            if (_currentEnforceLibraryOrder != GetOptions().UIFunctionOptions.EnforceLibraryOrder)
            {
                _currentEnforceLibraryOrder = GetOptions().UIFunctionOptions.EnforceLibraryOrder;

                if (_currentEnforceLibraryOrder)
                {
                    EnforceLibraryOrder.Patch();
                }
                else
                {
                    EnforceLibraryOrder.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("BeautifyMissingMetadata is set to {0}", options.UIFunctionOptions.BeautifyMissingMetadata);
            if (_currentBeautifyMissingMetadata != GetOptions().UIFunctionOptions.BeautifyMissingMetadata)
            {
                _currentBeautifyMissingMetadata = GetOptions().UIFunctionOptions.BeautifyMissingMetadata;

                if (_currentBeautifyMissingMetadata)
                {
                    BeautifyMissingMetadata.Patch();
                }
                else
                {
                    BeautifyMissingMetadata.Unpatch();
                }
            }

            if (!suppressLogger)
                logger.Info("CatchupMode is set to {0}", options.GeneralOptions.CatchupMode);
            if (_currentCatchupMode != options.GeneralOptions.CatchupMode)
            {
                _currentCatchupMode = options.GeneralOptions.CatchupMode;

                if (options.GeneralOptions.CatchupMode)
                {
                    InitializeCatchupMode();
                }
                else
                {
                    DisposeCatchupMode();
                }
            }

            if (!suppressLogger)
            {
                logger.Info("EnableIntroSkip is set to {0}", options.IntroSkipOptions.EnableIntroSkip);
                logger.Info("MaxIntroDurationSeconds is set to {0}", options.IntroSkipOptions.MaxIntroDurationSeconds);
                logger.Info("MaxCreditsDurationSeconds is set to {0}", options.IntroSkipOptions.MaxCreditsDurationSeconds);
                logger.Info("MinOpeningPlotDurationSeconds is set to {0}", options.IntroSkipOptions.MinOpeningPlotDurationSeconds);
            }
            if (_currentEnableIntroSkip != options.IntroSkipOptions.EnableIntroSkip)
            {
                _currentEnableIntroSkip = options.IntroSkipOptions.EnableIntroSkip;
                if (options.IntroSkipOptions.EnableIntroSkip)
                {
                    PlaySessionMonitor.Initialize();
                }
                else
                {
                    PlaySessionMonitor.Dispose();
                }
            }

            if (!suppressLogger)
            {
                var intoSkipLibraryScope = string.Join(", ",
                    options.IntroSkipOptions.LibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => options.IntroSkipOptions.LibraryList
                            .FirstOrDefault(option => option.Value == v)
                            ?.Name) ?? Enumerable.Empty<string>());
                logger.Info("IntroSkip - LibraryScope is set to {0}",
                        string.IsNullOrEmpty(intoSkipLibraryScope) ? "ALL" : intoSkipLibraryScope);

                var introSkipUserScope = string.Join(", ",
                    options.IntroSkipOptions.UserScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => options.IntroSkipOptions.UserList
                            .FirstOrDefault(option => option.Value == v)
                            ?.Name) ?? Enumerable.Empty<string>());
                logger.Info("IntroSkip - UserScope is set to {0}",
                        string.IsNullOrEmpty(introSkipUserScope) ? "ALL" : introSkipUserScope);
            }
            PlaySessionMonitor.UpdateLibraryPathsInScope();
            PlaySessionMonitor.UpdateUsersInScope();
            
            if (!suppressLogger)
                logger.Info("UnlockIntroSkip is set to {0}", options.IntroSkipOptions.UnlockIntroSkip);
            if (_currentUnlockIntroSkip != options.IntroSkipOptions.UnlockIntroSkip)
            {
                _currentUnlockIntroSkip = options.IntroSkipOptions.UnlockIntroSkip;
                if (options.IntroSkipOptions.IsModSupported)
                {
                    if (_currentUnlockIntroSkip)
                    {
                        UnlockIntroSkip.Patch();
                    }
                    else
                    {
                        UnlockIntroSkip.Unpatch();
                    }
                }
            }

            if (!suppressLogger)
            {
                var markerEnabledLibraryScope = string.Join(", ",
                    options.IntroSkipOptions.MarkerEnabledLibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => options.IntroSkipOptions.MarkerEnabledLibraryList
                            .FirstOrDefault(option => option.Value == v)
                            ?.Name) ?? Enumerable.Empty<string>());
                logger.Info("Fingerprint - MarkerEnabledLibraryScope is set to {0}",
                    string.IsNullOrEmpty(markerEnabledLibraryScope) ? "ALL" : markerEnabledLibraryScope);
                logger.Info("IntroDetectionFingerprintMinutes is set to {0}",
                    options.IntroSkipOptions.IntroDetectionFingerprintMinutes);
            }
            ChapterApi.UpdateLibraryIntroDetectionFingerprintLength();

            if (!suppressLogger)
            {
                var searchScope = string.Join(", ",
                    options.ModOptions.SearchScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s =>
                            Enum.TryParse(s.Trim(), true, out SearchItemType type)
                                ? EnumExtensions.GetDescription(type)
                                : null)
                        .Where(d => d != null));
                logger.Info("EnhanceChineseSearch - SearchScope is set to {0}",
                        string.IsNullOrEmpty(searchScope) ? "ALL" : searchScope);
            }
            if (_currentSearchScope != options.ModOptions.SearchScope)
            {
                _currentSearchScope = options.ModOptions.SearchScope;

                if (options.ModOptions.EnhanceChineseSearch)
                    EnhanceChineseSearch.UpdateSearchScope();
            }

            if (suppressLogger) _currentSuppressOnOptionsSaved = false;

            base.OnOptionsSaved(options);
        }

        protected override PluginOptions OnBeforeShowUI(PluginOptions options)
        {
            var libraries = _libraryManager.GetVirtualFolders();

            var list = new List<EditorSelectOption>();
            var listShow = new List<EditorSelectOption>();
            var listMarkerEnabled = new List<EditorSelectOption>();

            list.Add(new EditorSelectOption
            {
                Value = "-1",
                Name = Resources.Favorites,
                IsEnabled = true
            });

            foreach (var item in libraries)
            {
                var selectOption = new EditorSelectOption
                {
                    Value = item.ItemId,
                    Name = item.Name,
                    IsEnabled = true,
                };

                list.Add(selectOption);

                if (item.CollectionType == "tvshows" || item.CollectionType is null) // null means mixed content library
                {
                    listShow.Add(selectOption);

                    if (item.LibraryOptions.EnableMarkerDetection)
                    {
                        listMarkerEnabled.Add(selectOption);
                    }
                    
                }
            }

            options.MediaInfoExtractOptions.LibraryList = list;
            options.IntroSkipOptions.LibraryList = listShow;
            options.IntroSkipOptions.MarkerEnabledLibraryList = listMarkerEnabled;

            var languageList = new List<EditorSelectOption>
            {
                new EditorSelectOption
                {
                    Value = "zh-cn",
                    Name = "zh-CN",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "zh-sg",
                    Name = "zh-SG",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "zh-hk",
                    Name = "zh-HK",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "zh-tw",
                    Name = "zh-TW",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "ja-jp",
                    Name = "ja-JP",
                    IsEnabled = true
                }
            };
            options.MetadataEnhanceOptions.LanguageList = languageList;

            options.AboutOptions.VersionInfoList.Clear();
            options.AboutOptions.VersionInfoList.Add(
                new GenericListItem
                {
                    PrimaryText = GetVersionHash(),
                    Icon = IconNames.info,
                    IconMode = ItemListIconMode.SmallRegular
                });

            options.AboutOptions.VersionInfoList.Add(
                new GenericListItem
                {
                    PrimaryText = Resources.Repo_Link,
                    Icon = IconNames.code,
                    IconMode = ItemListIconMode.SmallRegular,
                    HyperLink = "https://github.com/sjtuross/StrmAssistant",
                });

            options.AboutOptions.VersionInfoList.Add(
                new GenericListItem
                {
                    PrimaryText = Resources.Wiki_Link,
                    Icon = IconNames.menu_book,
                    IconMode = ItemListIconMode.SmallRegular,
                    HyperLink = "https://github.com/sjtuross/StrmAssistant/wiki",
                });

            var allUsers = LibraryApi.AllUsers;
            var userList = new List<EditorSelectOption>();
            foreach (var user in allUsers)
            {
                var selectOption = new EditorSelectOption
                {
                    Value = user.Key.InternalId.ToString(),
                    Name = (user.Value ? "\ud83d\udc51" : "\ud83d\udc64") + user.Key.Name,
                    IsEnabled = true,
                };

                userList.Add(selectOption);
            }

            options.IntroSkipOptions.UserList = userList;

            var searchItemTypeList = new List<EditorSelectOption>();
            foreach (Enum item in Enum.GetValues(typeof(SearchItemType)))
            {
                var selectOption = new EditorSelectOption
                {
                    Value = item.ToString(),
                    Name=EnumExtensions.GetDescription(item),
                    IsEnabled = true,
                };

                searchItemTypeList.Add(selectOption);
            }

            options.ModOptions.SearchItemTypeList = searchItemTypeList;

            return base.OnBeforeShowUI(options);
        }

        protected override void OnCreatePageInfo(PluginPageInfo pageInfo)
        {
            pageInfo.Name = Resources.PluginOptions_EditorTitle_Strm_Assistant;
            pageInfo.EnableInMainMenu = true;
            pageInfo.MenuIcon = "video_settings";

            base.OnCreatePageInfo(pageInfo);
        }

        private static string GetVersionHash()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            var fullVersion = assembly.GetName().Version?.ToString();

            if (informationalVersion != null)
            {
                var parts = informationalVersion.Split('+');
                var shortCommitHash = parts.Length > 1 ? parts[1].Substring(0, 7) : "n/a";
                return $"{fullVersion}+{shortCommitHash}";
            }

            return fullVersion;
        }
    }
}
