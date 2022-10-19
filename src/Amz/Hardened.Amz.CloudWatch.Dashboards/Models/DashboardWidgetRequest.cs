using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Amz.CloudWatch.Dashboards.Models;

public partial record DashboardWidgetRequest(bool Describe, DashboardWidgetContext WidgetContext);
