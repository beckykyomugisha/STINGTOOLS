using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Phase 175 audit P0-2 — refresh + invitation tokens are now stored
/// SHA-256 hashed, never plaintext. We can't recover the original
/// secrets to hash them in place, so the only safe path is to
/// invalidate the existing rows: every active session re-logs in,
/// pending invitations need to be re-sent.
///
/// Password-reset tokens (RESET: prefix) were already hashed at
/// rest, so they're left alone.
/// </summary>
public partial class HashRefreshTokens : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // Clear the column for any value that isn't already a hashed
        // RESET: token. Both raw refresh sessions and INV: invitations
        // get nulled out — users get a 401 on next request (mobile +
        // dashboard handle this and prompt re-login); BIM Managers
        // re-invite the few pending users.
        mb.Sql(@"
            UPDATE ""Users""
               SET ""RefreshToken"" = NULL,
                   ""RefreshTokenExpiresAt"" = NULL
             WHERE ""RefreshToken"" IS NOT NULL
               AND ""RefreshToken"" NOT LIKE 'RESET:%';
        ");
    }

    protected override void Down(MigrationBuilder mb)
    {
        // No-op — we can't reconstruct the cleared tokens.
    }
}
