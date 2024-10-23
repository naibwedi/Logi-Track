using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using logirack.Models;
using Microsoft.AspNetCore.Identity;
//ghazi code
public class ApprovedOnlyAttribute : AuthorizeAttribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity.IsAuthenticated)
        {
            var userManager = context.HttpContext.RequestServices.GetService(typeof(UserManager<ApplicationUser>)) as UserManager<ApplicationUser>;
            var userId = userManager.GetUserId(context.HttpContext.User);
            var appUser = userManager.FindByIdAsync(userId).Result;
        }
    }
}