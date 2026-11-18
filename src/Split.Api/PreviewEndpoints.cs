using Split.Domain;

namespace Split.Api;

/// <summary>
/// One stateless endpoint behind the demo on the landing page.
///
/// It exists so the demo runs the <em>real</em> settlement code. Working the answer out in
/// TypeScript in the browser would have meant two implementations of the one genuinely hard
/// thing in this project, and the one on the marketing page would have been the untested one.
/// This way the page cannot show something the server would disagree with.
///
/// Nothing is stored and no account is needed.
/// </summary>
public static class PreviewEndpoints
{
    /// <summary>Enough for any demo, and small enough that nobody can use this as a free solver.</summary>
    private const int MaxPeople = 12;

    public static void MapPreviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/settlement-preview", Preview);
    }

    private static IResult Preview(SettlementPreviewRequest request)
    {
        PreviewBalance[] input = request?.Balances ?? [];

        if (input.Length is 0 or > MaxPeople)
        {
            return Results.BadRequest(new { error = $"Send between 1 and {MaxPeople} balances." });
        }

        if (input.Any(b => string.IsNullOrWhiteSpace(b.Name)))
        {
            return Results.BadRequest(new { error = "Every balance needs a name." });
        }

        if (input.Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Length)
        {
            return Results.BadRequest(new { error = "Names must be distinct." });
        }

        // The domain speaks in participant ids, so map names onto synthetic ones for the call
        // and back again for the response. Deterministic ids keep the output stable.
        var ids = new Dictionary<string, Guid>(input.Length);
        var names = new Dictionary<Guid, string>(input.Length);

        for (int i = 0; i < input.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            ids[input[i].Name] = id;
            names[id] = input[i].Name;
        }

        Balance[] balances = [.. input.Select(b => new Balance(ids[b.Name], b.Net))];

        IReadOnlyList<Transfer> payments;
        try
        {
            payments = DebtSimplifier.Simplify(balances);
        }
        catch (ArgumentException error)
        {
            // Balances that do not sum to zero, or sub-cent amounts. The domain's own message
            // explains it better than anything this layer could invent — but ArgumentException
            // tacks "(Parameter 'balances')" onto it, which means nothing to someone poking at
            // a demo, so trim that off.
            string message = error.Message.Split(" (Parameter", StringSplitOptions.None)[0].Trim();
            return Results.BadRequest(new { error = message });
        }

        // What settling "the obvious way" would have cost: every debtor paying every creditor.
        int naive = input.Count(b => b.Net < 0) * input.Count(b => b.Net > 0);

        return Results.Ok(new SettlementPreviewResponse(
            [.. payments.Select(p => new PreviewPayment(
                names[p.FromParticipantId],
                names[p.ToParticipantId],
                p.Amount))],
            naive));
    }
}
