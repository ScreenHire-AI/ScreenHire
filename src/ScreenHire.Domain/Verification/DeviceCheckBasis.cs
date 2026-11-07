namespace ScreenHire.Domain.Verification;

/// <summary>
/// On which footing the careers page may compute a device fingerprint (docs/research/legal.md, "Device / browser
/// fingerprint"; verification-engine.md §3). ePrivacy Art. 5(3) needs consent for fingerprinting unless it is strictly
/// necessary, and data protection authorities differ on whether fraud prevention is, so the basis is configuration per
/// country rather than code. An organization may also choose one, but only to be more protective than its region
/// (<see cref="DeviceCheckBases.MoreProtective"/>).
/// </summary>
public enum DeviceCheckBasis
{
    /// <summary>No device fingerprint at all.</summary>
    Off,

    /// <summary>After the notice, with no separate opt-in: legitimate interest, or "strictly necessary" for fraud prevention.</summary>
    Notice,

    /// <summary>Only when the candidate ticks the device-check box on the form.</summary>
    Consent,
}

public static class DeviceCheckBases
{
    /// <summary>
    /// The more protective of two bases: <see cref="DeviceCheckBasis.Off"/>, then <see cref="DeviceCheckBasis.Consent"/>,
    /// then <see cref="DeviceCheckBasis.Notice"/>. A region's basis is a floor an organization's own choice cannot lower.
    /// </summary>
    public static DeviceCheckBasis MoreProtective(DeviceCheckBasis a, DeviceCheckBasis b) =>
        Protectiveness(a) >= Protectiveness(b) ? a : b;

    private static int Protectiveness(DeviceCheckBasis basis) => basis switch
    {
        DeviceCheckBasis.Off => 2,
        DeviceCheckBasis.Consent => 1,
        _ => 0,
    };
}
