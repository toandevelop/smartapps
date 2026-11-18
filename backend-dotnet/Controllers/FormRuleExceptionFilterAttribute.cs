using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

public sealed class FormRuleExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is FormRuleValidationException validation)
            context.Result = new ObjectResult(new { success = false, message = validation.Message, errors = validation.Errors }) { StatusCode = 422 };
        else if (context.Exception is FormRuleConfigurationException configuration)
            context.Result = new BadRequestObjectResult(new { success = false, message = configuration.Message });
        else return;
        context.ExceptionHandled = true;
    }
}
