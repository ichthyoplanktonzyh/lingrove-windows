namespace Cotabby.Core.Review;

/// <summary>Separate from inference tokens: Anki stores the exact supplied key, with only an empty
/// string deleting it. The shared settings owner borrows this effect boundary on explicit actions.
/// The application owns the store; constructing preferences must never read a secret.</summary>
public interface IAnkiCredentialStore
{
    string ReadKey();
    void SaveKey(string value);
}
