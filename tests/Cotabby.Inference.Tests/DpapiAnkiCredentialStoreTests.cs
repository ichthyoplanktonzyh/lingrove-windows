using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Cotabby.Core.Review;
using Cotabby.Win32.Security;

namespace Cotabby.Inference.Tests;

/// <summary>Actual current-user DPAPI and filesystem tests in disposable folders. No user key
/// location, settings file, HTTP endpoint, Keychain, clipboard or interactive desktop is accessed.</summary>
public sealed class DpapiAnkiCredentialStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-anki-key-" + Guid.NewGuid().ToString("N"));
    private string KeyPath => Path.Combine(_root, "private", "nested", "anki-credential.bin");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    private DpapiAnkiCredentialStore Store() => new(KeyPath);
    private static void Failure(Action action)
    {
        var error = Assert.Throws<AnkiReviewException>(action);
        Assert.Equal(DpapiAnkiCredentialStore.FailureMessage, error.Message); Assert.Null(error.InnerException);
        Assert.Equal(AnkiReviewFailure.CredentialUnavailable, error.Failure);
    }
    [Fact]
    public void ConstructionAndMissingReadOrDeleteDoNotCreateAFileOrDirectory()
    {
        var store = Store(); Assert.False(Directory.Exists(_root));
        Assert.Equal("", store.ReadKey()); store.SaveKey(""); store.SaveKey("");
        Assert.False(Directory.Exists(_root));
        Assert.Equal("anki-credential.bin", Path.GetFileName(DpapiAnkiCredentialStore.DefaultFilePath));
        Assert.NotEqual("endpoint-credential.bin", Path.GetFileName(DpapiAnkiCredentialStore.DefaultFilePath));
    }
    [Theory]
    [InlineData("  synthetic-key  ")]
    [InlineData(" \t\r\n")]
    [InlineData("a\0b")]
    [InlineData("e\u0301\uFEFF🎉拾语\n")]
    public void ExactKeyRoundTripsThroughCurrentUserCiphertextAndReopenedOwner(string key)
    {
        var store = Store(); store.SaveKey(key);
        Assert.Equal(key, Store().ReadKey());
        byte[] encrypted = File.ReadAllBytes(KeyPath);
        Assert.DoesNotContain(key, Encoding.Latin1.GetString(encrypted));
        byte[] plain = ProtectedData.Unprotect(encrypted, DpapiAnkiCredentialStore.Entropy.ToArray(), DataProtectionScope.CurrentUser);
        try { Assert.Equal(Encoding.UTF8.GetBytes(key), plain); } finally { CryptographicOperations.ZeroMemory(plain); }
        Assert.Throws<CryptographicException>(() => ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
        store.SaveKey("replacement"); Assert.Equal("replacement", Store().ReadKey());
        store.SaveKey(""); Assert.False(File.Exists(KeyPath)); Assert.Equal("", Store().ReadKey());
        Assert.Empty(Directory.GetFiles(_root, ".anki-key-*", SearchOption.AllDirectories));
    }
    [Fact]
    public void NewParentAndReplacedCiphertextHaveProtectedCurrentUserOnlyAccess()
    {
        Directory.CreateDirectory(_root); byte[] previous = new DirectoryInfo(_root).GetAccessControl().GetSecurityDescriptorBinaryForm();
        Store().SaveKey("first"); Store().SaveKey("second"); using var identity = WindowsIdentity.GetCurrent();
        foreach (string path in new[] { Path.Combine(_root, "private"), Path.GetDirectoryName(KeyPath)! })
        {
            var security = new DirectoryInfo(path).GetAccessControl(); Assert.True(security.AreAccessRulesProtected);
            Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.All(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule => Assert.Equal(identity.User, rule.IdentityReference));
        }
        var file = new FileInfo(KeyPath).GetAccessControl(); Assert.True(file.AreAccessRulesProtected);
        Assert.Equal(identity.User, file.GetOwner(typeof(SecurityIdentifier)));
        Assert.All(file.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule =>
        { Assert.Equal(identity.User, rule.IdentityReference); Assert.Equal(AccessControlType.Allow, rule.AccessControlType); });
        Assert.Equal(previous, new DirectoryInfo(_root).GetAccessControl().GetSecurityDescriptorBinaryForm());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptCiphertextOrDecryptedInvalidUtf8FailsWithoutChangingBytes(bool validProtection)
    {
        Store().SaveKey("original");
        byte[] bytes = validProtection ? ProtectedData.Protect([0xff, 0xfe], DpapiAnkiCredentialStore.Entropy.ToArray(), DataProtectionScope.CurrentUser) : [0, 1, 2, 3];
        File.WriteAllBytes(KeyPath, bytes); Failure(() => Store().ReadKey()); Assert.Equal(bytes, File.ReadAllBytes(KeyPath));
    }
    [Fact]
    public void InvalidUtf16FailsBeforeChangingThePreviousKeyOrCreatingAStage()
    {
        Store().SaveKey("original"); byte[] previous = File.ReadAllBytes(KeyPath);
        Failure(() => Store().SaveKey("bad\uD800")); Assert.Equal(previous, File.ReadAllBytes(KeyPath)); Assert.Equal("original", Store().ReadKey());
        Failure(() => Store().SaveKey(null!)); Assert.Equal(previous, File.ReadAllBytes(KeyPath));
        Assert.Empty(Directory.GetFiles(_root, ".anki-key-*", SearchOption.AllDirectories));
    }
    [Fact]
    public void LockedDestinationMakesReadReplaceAndDeleteFailGenericallyAndPreservesPreviousKey()
    {
        Store().SaveKey("original");
        using (var locked = new FileStream(KeyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Failure(() => Store().ReadKey()); Failure(() => Store().SaveKey("next")); Failure(() => Store().SaveKey(""));
        }
        Assert.Equal("original", Store().ReadKey()); Assert.Empty(Directory.GetFiles(_root, ".anki-key-*", SearchOption.AllDirectories));
    }
    [Fact]
    public void ADirectoryAtTheCredentialLeafIsFailureRatherThanAnAbsentKey()
    {
        Directory.CreateDirectory(KeyPath);
        Failure(() => Store().ReadKey()); Failure(() => Store().SaveKey("next")); Failure(() => Store().SaveKey(""));
        Assert.True(Directory.Exists(KeyPath)); Assert.Empty(Directory.GetFiles(_root, ".anki-key-*", SearchOption.AllDirectories));
    }
    [Fact]
    public void AnInferenceCredentialCannotBeReadAsAnAnkiKeyAndRemainsUnchanged()
    {
        string endpoint = Path.Combine(_root, "endpoint-credential.bin");
        new DpapiOpenAICompatibleCredentialStore(endpoint).SaveApiKey("  inference-key  ");
        byte[] previous = File.ReadAllBytes(endpoint); Failure(() => new DpapiAnkiCredentialStore(endpoint).ReadKey());
        Assert.Equal(previous, File.ReadAllBytes(endpoint)); Assert.Equal("inference-key", new DpapiOpenAICompatibleCredentialStore(endpoint).ReadApiKey());
        Store().SaveKey("  anki-key  "); Assert.Equal("  anki-key  ", Store().ReadKey()); Assert.Equal(previous, File.ReadAllBytes(endpoint));
    }
    [Fact]
    public void AStagingNameCollisionCannotDeleteAnotherFileOrReplaceTheExistingKey()
    {
        Store().SaveKey("original"); var identity = Guid.NewGuid(); string collision = Path.Combine(Path.GetDirectoryName(KeyPath)!, ".anki-key-" + identity.ToString("N"));
        File.WriteAllText(collision, "preserved peer bytes");
        Failure(() => new DpapiAnkiCredentialStore(KeyPath, () => identity).SaveKey("next"));
        Assert.Equal("preserved peer bytes", File.ReadAllText(collision)); Assert.Equal("original", Store().ReadKey());
    }
    [Fact]
    public void TheFlushedStageRemainsPinnedUntilHandlePublicationAndFailureDeletesOnlyOwnedStage()
    {
        Store().SaveKey("original");
        var next = new DpapiAnkiCredentialStore(KeyPath, Guid.NewGuid, stage =>
        {
            Assert.Throws<IOException>(() => File.Move(stage, stage + ".replaced"));
            string parent = Path.GetDirectoryName(KeyPath)!;
            Assert.Throws<IOException>(() => Directory.Move(parent, parent + "-relocated"));
            throw new IOException("synthetic after-flush failure");
        });
        Failure(() => next.SaveKey("next")); Assert.Equal("original", Store().ReadKey());
        Assert.Empty(Directory.GetFiles(_root, ".anki-key-*", SearchOption.AllDirectories));
    }
}
