﻿using Hardened.IntegrationTests.Web.Lambda.SUT;
using Hardened.Shared.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]