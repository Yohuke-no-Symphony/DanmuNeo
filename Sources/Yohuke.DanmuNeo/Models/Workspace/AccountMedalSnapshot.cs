namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>绑定凭据撤销令牌的牌墙更新，不携带凭据内容。</summary>
public sealed record AccountMedalSnapshot(string AccountId, AccountMedalCollection Collection,
    CancellationToken CredentialCancellationToken);
