namespace InfraPilot.Web.Pages.Account;

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly OperatorAuthOptions _options;

    public LoginModel(IOptions<OperatorAuthOptions> options)
    {
        _options = options.Value;
    }

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(Input.Username, _options.Username, StringComparison.Ordinal)
            || !string.Equals(Input.Password, _options.Password, StringComparison.Ordinal))
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, _options.Username),
            new Claim(ClaimTypes.Role, "Operator")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true
            });

        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    }

    public sealed class LoginInputModel
    {
        [Required]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }
}
