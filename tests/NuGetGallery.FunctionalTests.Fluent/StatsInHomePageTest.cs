﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGetGallery.FunctionTests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAutomation;

namespace NuGetGallery.FunctionalTests.Fluent
{
    [TestClass]
    public class StatsInHomePageTest : NuGetFluentTest
    {
        [TestMethod]
        [Description("Cross-check the contents of the Statistics page against the last6weeks API endpoint.")]
        public void StatsInHomePage()
        {
            // Request the last 6 weeks endpoint.
            WebRequest request = WebRequest.Create(UrlHelper.BaseUrl + @"/stats/totals");
            // Get the response.          
            WebResponse response = request.GetResponse();
            StreamReader sr = new StreamReader(response.GetResponseStream());
            string responseText = sr.ReadToEnd();

            // Extract the substrings we'll search for on the front page.
            string downloads = responseText.Substring(responseText.IndexOf(@"Downloads"":""") + 12);
            downloads = downloads.Substring(0, downloads.IndexOf(@""""));
            string uniquePackages = responseText.Substring(responseText.IndexOf(@"UniquePackages"":""") + 17);
            uniquePackages = uniquePackages.Substring(0, uniquePackages.IndexOf(@""""));
            string totalPackages = responseText.Substring(responseText.IndexOf(@"TotalPackages"":""") + 16);
            totalPackages = totalPackages.Substring(0, totalPackages.IndexOf(@""""));

            I.Open(UrlHelper.BaseUrl);
            I.Wait(5);
            I.Expect.Text(downloads).In("#Downloads");
            I.Expect.Text(uniquePackages).In("#UniquePackages");
            I.Expect.Text(totalPackages).In("#TotalPackages");
        }
    }
}
