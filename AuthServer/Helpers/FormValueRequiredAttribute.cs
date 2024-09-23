using System;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;

namespace AuthServer.Helpers;

public class FormValueRequiredAttribute(string name) : ActionMethodSelectorAttribute
{
	public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
	{
		return !string.Equals(routeContext.HttpContext.Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(routeContext.HttpContext.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(routeContext.HttpContext.Request.Method, "DELETE", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(routeContext.HttpContext.Request.Method, "TRACE", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrEmpty(routeContext.HttpContext.Request.ContentType)
			&& routeContext.HttpContext.Request.ContentType.StartsWith(
				"application/x-www-form-urlencoded",
				StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrEmpty(routeContext.HttpContext.Request.Form[name]);
	}
}
