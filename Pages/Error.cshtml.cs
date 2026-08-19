using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LumaCast.Pages;

/// <summary>Modelo da página exibida quando ocorre uma falha não tratada.</summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    /// <summary>Obtém o identificador da requisição usado para correlação em logs.</summary>
    public string? RequestId { get; set; }

    /// <summary>Indica se há um identificador de requisição que possa ser exibido.</summary>
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>Captura o identificador da atividade ou da requisição HTTP atual.</summary>
    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
