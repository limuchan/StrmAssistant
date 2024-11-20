using HarmonyLib;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using static StrmAssistant.CommonUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class AltMovieDbConfig
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        
        private static Assembly _movieDbAssembly;
        private static MethodInfo _getMovieDbResponse;
        private static MethodInfo _convertImageToLocal;

        private static readonly string DefaultMovieDbApiUrl = "https://api.themoviedb.org";
        private static readonly string DefaultAltMovieDbApiUrl = "https://api.tmdb.org";
        private static readonly string DefaultMovieDbImageUrl = "https://image.tmdb.org";
        private static string SystemDefaultMovieDbApiKey;

        public static void Initialize()
        {
            try
            {
                _movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (_movieDbAssembly != null)
                {
                    var movieDbProviderBase = _movieDbAssembly.GetType("MovieDb.MovieDbProviderBase");
                    _getMovieDbResponse = movieDbProviderBase.GetMethod("GetMovieDbResponse",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var apiKey = movieDbProviderBase.GetField("ApiKey", BindingFlags.Static | BindingFlags.NonPublic);
                    SystemDefaultMovieDbApiKey = apiKey?.GetValue(null) as string;

                    var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                    var libraryManager =
                        embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.Library.LibraryManager");
                    _convertImageToLocal= libraryManager.GetMethod("ConvertImageToLocal",
                        BindingFlags.Public | BindingFlags.Instance);
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("AltMovieDbConfig - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.AltMovieDbConfig)
            {
                PatchApiUrl();

                if (!string.IsNullOrEmpty(Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.AltMovieDbImageUrl))
                {
                    PatchImageUrl();
                }
            }
        }

        public static void PatchApiUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony && _movieDbAssembly != null)
            {
                try
                {
                    if (!IsPatched(_getMovieDbResponse, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Patch(_getMovieDbResponse,
                            prefix: new HarmonyMethod(typeof(AltMovieDbConfig).GetMethod(
                                "GetMovieDbResponsePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch GetMovieDbResponse Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch GetMovieDbResponsePrefix Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void UnpatchApiUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_getMovieDbResponse, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Unpatch(_getMovieDbResponse,
                            AccessTools.Method(typeof(AltMovieDbConfig), "GetMovieDbResponsePrefix"));
                        Plugin.Instance.logger.Debug("Unpatch GetMovieDbResponse Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch GetMovieDbResponse Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        public static void PatchImageUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony && _movieDbAssembly != null)
            {
                try
                {
                    if (!IsPatched(_convertImageToLocal, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Patch(_convertImageToLocal,
                            prefix: new HarmonyMethod(typeof(AltMovieDbConfig).GetMethod(
                                "ConvertImageToLocalPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch ConvertImageToLocal Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch ConvertImageToLocal Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void UnpatchImageUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_convertImageToLocal, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Unpatch(_convertImageToLocal,
                            AccessTools.Method(typeof(AltMovieDbConfig), "ConvertImageToLocalPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch ConvertImageToLocal Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch ConvertImageToLocal Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool GetMovieDbResponsePrefix(HttpRequestOptions options)
        {
            var metadataEnhanceOptions = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var apiUrl = metadataEnhanceOptions.AltMovieDbApiUrl;
            var apiKey = metadataEnhanceOptions.AltMovieDbApiKey;

            var requestUrl = options.Url;

            if (requestUrl.StartsWith(DefaultMovieDbApiUrl + "/3/configuration", StringComparison.Ordinal))
            {
                requestUrl = requestUrl.Replace(DefaultMovieDbApiUrl, DefaultAltMovieDbApiUrl);
            }
            else if (IsValidHttpUrl(apiUrl))
            {
                requestUrl = requestUrl.Replace(DefaultMovieDbApiUrl, apiUrl);
            }

            if (IsValidMovieDbApiKey(apiKey))
            {
                requestUrl = requestUrl.Replace(SystemDefaultMovieDbApiKey, apiKey);
            }

            if (!string.Equals(requestUrl, options.Url, StringComparison.Ordinal))
            {
                options.Url = requestUrl;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool ConvertImageToLocalPrefix(BaseItem item, ItemImageInfo image, int imageIndex,
            CancellationToken cancellationToken)
        {
            var imageUrl = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.AltMovieDbImageUrl;

            if (IsValidHttpUrl(imageUrl))
            {
                image.Path = image.Path.Replace(DefaultMovieDbImageUrl, imageUrl);
            }
            
            return true;
        }
    }
}
