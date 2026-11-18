namespace Split.Api.Data;

/// <summary>
/// Somebody who has signed in with Google.
///
/// Note what is not here: no password, no password hash, no reset token, no lockout counter.
/// Delegating sign-in to Google means this application never handles a credential, which is
/// the main reason to do it that way.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Google's <c>sub</c> claim: the stable, unique id for this account.
    ///
    /// This, and not the email address, is the identity. Email addresses can be changed and
    /// can be reassigned between people inside a Workspace domain; matching on one would let
    /// whoever inherits an address inherit the groups too. <c>sub</c> never changes and is
    /// never reused.
    /// </summary>
    public required string GoogleSubject { get; set; }

    /// <summary>Kept for display only, and refreshed on each sign-in.</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Google's profile picture URL. May be null; the UI falls back to initials.</summary>
    public string? AvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A group that has shown up in somebody's history.
///
/// This is deliberately <em>not</em> an access control record. Groups stay reachable by their
/// link, signed in or not — see the README. All this does is remember which groups a user has
/// created or opened, so they get a list instead of having to keep the URLs themselves.
/// </summary>
public class GroupMembership
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public Guid GroupId { get; set; }

    /// <summary>True for whoever created the group. Shown in the list; grants nothing.</summary>
    public bool IsCreator { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
