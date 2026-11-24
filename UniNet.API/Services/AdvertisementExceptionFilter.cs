using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UniNet.Application.Common;

namespace UniNet.API.Services;
public sealed class AdvertisementExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not AdvertisementException adEx) return;

        context.Result = new ObjectResult(new
        {
            code = adEx.Code,
            message = adEx.Message,
            statusCode = adEx.StatusCode
        })
        {
            StatusCode = adEx.StatusCode
        };

        context.ExceptionHandled = true;
    }
}

