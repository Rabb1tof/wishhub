namespace WishHub.Parsing.Playwright.Fingerprinting;

public interface IFingerprintPool
{
    IReadOnlyList<FingerprintProfile> Profiles { get; }

    FingerprintProfile GetRandom();
}
