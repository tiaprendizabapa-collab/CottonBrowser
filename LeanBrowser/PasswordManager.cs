using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

/// <summary>
/// O runtime cuida da detecção de formulários, consentimento, armazenamento
/// protegido pelo Windows e preenchimento. Nunca expõe senhas ao host C#.
/// As configurações são compartilhadas por todas as abas do mesmo perfil.
/// </summary>
public static class PasswordManager
{
    public static void Configure(CoreWebView2 core)
    {
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = true;
        core.Settings.IsReputationCheckingRequired = true;
    }

    // Desativar autosave sozinho NÃO desativa preenchimento de dados já salvos.
    public static Task ClearSavedPasswordsAsync(CoreWebView2 core) =>
        core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.PasswordAutosave);
}
