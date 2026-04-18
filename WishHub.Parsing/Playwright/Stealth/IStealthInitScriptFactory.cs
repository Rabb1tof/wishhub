using WishHub.Parsing.Playwright.Fingerprinting;

namespace WishHub.Parsing.Playwright.Stealth;

public interface IStealthInitScriptFactory
{
    string Create(FingerprintProfile profile);
}
