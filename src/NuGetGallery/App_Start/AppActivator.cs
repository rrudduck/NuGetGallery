﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.IO;
using System.Security.Claims;
using System.Web.Helpers;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.UI;
using Elmah;
using Elmah.Contrib.Mvc;
using Microsoft.Web.Infrastructure.DynamicModuleHelper;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Ninject;
using Ninject.Web.Common;
using NuGetGallery;
using NuGetGallery.Configuration;
using NuGetGallery.Infrastructure;
using NuGetGallery.Infrastructure.Jobs;
using NuGetGallery.Jobs;
using WebActivator;
using WebBackgrounder;

[assembly: WebActivator.PreApplicationStartMethod(typeof(AppActivator), "PreStart")]
[assembly: PostApplicationStartMethod(typeof(AppActivator), "PostStart")]
[assembly: ApplicationShutdownMethod(typeof(AppActivator), "Stop")]

namespace NuGetGallery
{
    public static class AppActivator
    {
        private static JobManager _jobManager;
        private static readonly Bootstrapper NinjectBootstrapper = new Bootstrapper();

        public static void PreStart()
        {
            AntiForgeryConfig.UniqueClaimTypeIdentifier = ClaimTypes.NameIdentifier;

            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(CreateViewEngine());

            NinjectPreStart();
            ElmahPreStart();
            GlimpsePreStart();

            try
            {
                if (RoleEnvironment.IsAvailable)
                {
                    CloudPreStart();
                }
            }
            catch (Exception)
            {
                // Azure SDK not available!
            }
        }

        public static void PostStart()
        {
            // Get configuration from the kernel
            var config = Container.Kernel.Get<IAppConfiguration>();
            BackgroundJobsPostStart(config);
            AppPostStart();
            BundlingPostStart();
        }

        public static void Stop()
        {
            BackgroundJobsStop();
            NinjectStop();
        }

        private static RazorViewEngine CreateViewEngine()
        {
            var ret = new RazorViewEngine();

            ret.AreaMasterLocationFormats = 
                ret.AreaViewLocationFormats =
                ret.AreaPartialViewLocationFormats =
                new string[]
            {
                "~/Areas/{2}/Views/{1}/{0}.cshtml",
                "~/Branding/Views/Shared/{0}.cshtml",
                "~/Areas/{2}/Views/Shared/{0}.cshtml",
            };

            ret.MasterLocationFormats = 
                ret.ViewLocationFormats  =
                ret.PartialViewLocationFormats =
                new string[]
            {
                "~/Branding/Views/{1}/{0}.cshtml",
                "~/Views/{1}/{0}.cshtml",
                "~/Branding/Views/Shared/{0}.cshtml",
                "~/Views/Shared/{0}.cshtml",
            };

            return ret;
        }

        private static void GlimpsePreStart()
        {
            DynamicModuleUtility.RegisterModule(typeof(Glimpse.AspNet.HttpModule));
        }

        private static void CloudPreStart()
        {
            Trace.Listeners.Add(new DiagnosticMonitorTraceListener());
        }

        private static void BundlingPostStart()
        {
            var jQueryBundle = new ScriptBundle("~/Scripts/jquery")
                .Include("~/Scripts/jquery-{version}.js");
            BundleTable.Bundles.Add(jQueryBundle);

            ScriptManager.ScriptResourceMapping.AddDefinition("jquery",
                new ScriptResourceDefinition
                {
                    Path = jQueryBundle.Path
                });

            var scriptBundle = new ScriptBundle("~/Scripts/all")
                .Include("~/Scripts/jquery-{version}.js")
                .Include("~/Scripts/jquery.validate.js")
                .Include("~/Scripts/jquery.validate.unobtrusive.js")
                .Include("~/Scripts/typeahead.bundle.js")
                .Include("~/Scripts/nugetgallery.js")
                .Include("~/Scripts/stats.js");
            BundleTable.Bundles.Add(scriptBundle);

            // Modernizr needs to be delivered at the top of the page but putting it in a bundle gets us a cache-buster.
            // TODO: Use minified modernizr!
            var modernizrBundle = new ScriptBundle("~/Scripts/modernizr")
                .Include("~/Scripts/modernizr-{version}.js");
            BundleTable.Bundles.Add(modernizrBundle);

            Bundle stylesBundle = new StyleBundle("~/Content/css");
            foreach (string filename in new[] {
                    "Site.css",
                    "Layout.css",
                    "PageStylings.css"
                })
            {
                stylesBundle
                    .Include("~/Content/" + filename)
                    .Include("~/Branding/Content/" + filename);
            }

            BundleTable.Bundles.Add(stylesBundle);

            // Needs a) a separate bundle because of relative pathing in the @font-face directive
            // b) To be a bundle for auto-selection of ".min.css"
            var fontAwesomeBundle = new StyleBundle("~/Content/font-awesome/css");
            fontAwesomeBundle.Include("~/Content/font-awesome/font-awesome.css");
            BundleTable.Bundles.Add(fontAwesomeBundle);
        }

        private static void ElmahPreStart()
        {
            ServiceCenter.Current = _ => Container.Kernel;
        }

        private static void AppPostStart()
        {
            Routes.RegisterRoutes(RouteTable.Routes);
            Routes.RegisterServiceRoutes(RouteTable.Routes);
            AreaRegistration.RegisterAllAreas();

            GlobalFilters.Filters.Add(new ElmahHandleErrorAttribute() { View = "~/Views/Errors/InternalError.cshtml" });
            GlobalFilters.Filters.Add(new ReadOnlyModeErrorFilter());
            GlobalFilters.Filters.Add(new AntiForgeryErrorFilter());
            ValueProviderFactories.Factories.Add(new HttpHeaderValueProviderFactory());
        }

        private static void BackgroundJobsPostStart(IAppConfiguration configuration)
        {
            var indexer = Container.Kernel.TryGet<IIndexingService>();
            var jobs = new List<IJob>();
            if (indexer != null)
            {
                indexer.RegisterBackgroundJobs(jobs, configuration);
            }
            if (!configuration.HasWorker)
            {
                jobs.Add(
                    new UpdateStatisticsJob(TimeSpan.FromMinutes(5), 
                        () => new EntitiesContext(configuration.SqlConnectionString, readOnly: false), 
                        timeout: TimeSpan.FromMinutes(5)));
            }

            if (jobs.AnySafe())
            {
                var jobCoordinator = new NuGetJobCoordinator();
                _jobManager = new JobManager(jobs, jobCoordinator)
                    {
                        RestartSchedulerOnFailure = true
                    };
                _jobManager.Fail(e => ErrorLog.GetDefault(null).Log(new Error(e)));
                _jobManager.Start();
            }
        }

        private static void BackgroundJobsStop()
        {
            if (_jobManager != null)
            {
                _jobManager.Dispose();
            }
        }

        private static void NinjectPreStart()
        {
            DynamicModuleUtility.RegisterModule(typeof(OnePerRequestHttpModule));
            DynamicModuleUtility.RegisterModule(typeof(NinjectHttpModule));
            NinjectBootstrapper.Initialize(() => Container.Kernel);
        }

        private static void NinjectStop()
        {
            NinjectBootstrapper.ShutDown();
        }
    }
}
