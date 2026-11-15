using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniNet.Domain.Entities;

namespace UniNet.Application.Services;

// Only stable, explicitly listed fields are copied from a fetched item.
public static class GitEvidenceFactsExtractor
{
    public static GitFrozenFacts ExtractV3(string category, string externalId, JsonElement item, string? scope)
    {
        if (category != "pull-request-files")
            throw new ArgumentException("Version 3 is reserved for pull request file evidence.", nameof(category));

        var baseFacts = ExtractV2(category, externalId, item, scope);
        string? Text(string name) =>
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        long? Count(string name) =>
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0 ? count : null;
        static string? Sha(string? value) =>
            value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;

        var filePathValue = Text("filename");
        var filePath = string.IsNullOrWhiteSpace(filePathValue) ? null : filePathValue;
        var previousFilePathValue = Text("previous_filename");
        var previousFilePath = string.IsNullOrWhiteSpace(previousFilePathValue) ? null : previousFilePathValue;
        var statusValue = Text("status");
        var status = statusValue is "added" or "removed" or "modified" or "renamed" ? statusValue : null;
        var blobSha = Sha(Text("sha"));
        var additions = Count("additions");
        var deletions = Count("deletions");
        var changes = Count("changes");
        var patchHash = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("patch", out var patch) &&
            patch.ValueKind == JsonValueKind.String
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(patch.GetString()!))).ToLowerInvariant()
                : null;
        var missing = baseFacts.Details!.MissingFacts.Where(x => x != "CategoryCoverage").ToList();
        if (filePath is null) missing.Add("FilePath");
        if (blobSha is null) missing.Add("FileBlobSha");
        if (status is null) missing.Add("FileStatus");
        if (additions is null) missing.Add("Additions");
        if (deletions is null) missing.Add("Deletions");
        if (changes is null) missing.Add("Changes");
        if (patchHash is null) missing.Add("PatchContentUnavailable");
        var relatedPullRequestNumber = long.TryParse(scope, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : (long?)null;
        if (relatedPullRequestNumber is null) missing.Add("PullRequestScope");
        var details = baseFacts.Details with
        {
            RelatedPullRequestNumber = relatedPullRequestNumber,
            MissingFacts = missing,
            FilePath = filePath,
            PreviousFilePath = previousFilePath,
            FileStatus = status,
            Additions = additions,
            Deletions = deletions,
            Changes = changes,
            BlobSha = blobSha,
            PatchSha256 = patchHash
        };
        return baseFacts with
        {
            SchemaVersion = 3,
            Completeness = GetCompleteness(missing),
            Details = details
        };
    }

    internal static string GetPullRequestFileExternalId(string filePath, string? scope)
    {
        if (string.IsNullOrWhiteSpace(filePath) || filePath.Length > 4096)
            throw new ArgumentException("Invalid pull request file path.", nameof(filePath));
        if (!long.TryParse(scope, NumberStyles.None, CultureInfo.InvariantCulture, out var pullRequestNumber) || pullRequestNumber <= 0)
            throw new ArgumentException("Invalid pull request number.", nameof(scope));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{pullRequestNumber.ToString(CultureInfo.InvariantCulture)}\n{filePath}"))).ToLowerInvariant();
    }

    internal static string SanitizeFetchedPayload(string category, JsonElement item)
    {
        if (category != "pull-request-files") return item.GetRawText();
        if (item.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Pull request file payload must be an object.", nameof(item));

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in item.EnumerateObject())
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, "patch")) continue;
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static GitFrozenFacts ExtractV2(string category, string externalId, JsonElement item, string? scope)
    {
        var v1 = Extract(category == "pull-request-commits" ? "commits" : category, externalId, item) with { Category = category };
        DateTimeOffset? Date(string name)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                return null;
            return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
        }
        string? Text(string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? NestedText(string parent, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(parent, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.String ? nested.GetString() : null;
        static string? Sha(string? value) => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
        var stateValue = Text("state");
        var state = stateValue is "open" or "closed" or "APPROVED" or "CHANGES_REQUESTED" or "COMMENTED" or "DISMISSED" or "PENDING" ? stateValue : null;
        bool? Boolean(string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        var mergedAt = Date("merged_at");
        var mergedAtProvided = item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("merged_at", out var mergedAtValue) &&
            (mergedAtValue.ValueKind == JsonValueKind.Null ||
             mergedAtValue.ValueKind == JsonValueKind.String && mergedAt is not null);
        var mergedValue = Boolean("merged");
        var merged = mergedValue ?? ((state is "open" or "closed") && mergedAtProvided ? mergedAt is not null : null);
        var missing = new List<string>();
        if (category is "commits" or "pull-request-commits" or "pull-requests" or "pull-request-reviews" or "issues" or "issue-events")
        {
            if (v1.Actors.Count == 0) missing.Add("ProviderActorId");
        }
        if (category is "commits" or "pull-request-commits" && v1.CommitterDate is null)
            missing.Add("CommitterDate");
        if (category == "pull-requests")
        {
            if (state is null) missing.Add("PullRequestState");
            if (Date("created_at") is null) missing.Add("CreatedAt");
            if (merged is null) missing.Add("MergedState");
            if (merged == true && mergedAt is null) missing.Add("MergedAt");
            if ((merged == true && state == "open") || (merged == false && mergedAt is not null))
                missing.Add("MergeStateTimestampMismatch");
        }
        else if (category == "pull-request-reviews")
        {
            if (state is null) missing.Add("ReviewState");
            if (Date("submitted_at") is null) missing.Add("SubmittedAt");
        }
        else if (category == "issues")
        {
            if (state is not ("open" or "closed")) missing.Add("IssueState");
            if (Date("created_at") is null) missing.Add("CreatedAt");
            if (state == "closed" && Date("closed_at") is null) missing.Add("ClosedAt");
            if (state == "open" && Date("closed_at") is not null) missing.Add("IssueStateTimestampMismatch");
        }
        else if (category == "issue-events")
        {
            if (Date("created_at") is null) missing.Add("CreatedAt");
        }
        else if (category is not ("commits" or "pull-request-commits" or "pull-requests" or "pull-request-reviews" or "issues" or "issue-events" or "pull-request-files"))
            missing.Add("CategoryCoverage");
        long? prNumber = category is "pull-request-reviews" or "pull-request-review-comments" or "pull-request-commits" &&
            long.TryParse(scope, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
        if (category is "pull-request-reviews" or "pull-request-commits" && prNumber is null) missing.Add("PullRequestScope");
        long? issueNumber = category == "issue-events" && long.TryParse(scope, NumberStyles.None, CultureInfo.InvariantCulture, out var issueId) && issueId > 0 ? issueId : null;
        if (category == "issue-events" && issueNumber is null) missing.Add("IssueScope");
        var kindValue = Text("event");
        var kind = kindValue is "closed" or "reopened" or "assigned" or "unassigned" or "labeled" or "unlabeled" ? kindValue : null;
        if (category == "issue-events" && kind is null) missing.Add("EventKind");
        bool? isPullRequest = category == "issues" && item.ValueKind == JsonValueKind.Object ? item.TryGetProperty("pull_request", out _) : null;
        var pullRequestNumber = category == "pull-requests" &&
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty("number", out var numberValue) &&
            numberValue.ValueKind == JsonValueKind.Number && numberValue.TryGetInt64(out var pullRequestNumberValue) && pullRequestNumberValue > 0
                ? pullRequestNumberValue
                : prNumber;
        if (category == "pull-requests" && pullRequestNumber is null) missing.Add("PullRequestNumber");
        var details = new GitFrozenDetails(Date("created_at"), Date("updated_at"), Date("closed_at"), mergedAt, Date("submitted_at"),
            state, merged, Sha(NestedText("base", "sha")), Sha(NestedText("head", "sha")), Sha(Text("commit_id")), pullRequestNumber, isPullRequest, missing, issueNumber, kind);
        return v1 with { SchemaVersion = 2, Details = details, Completeness = GetCompleteness(missing) };
    }

    private static string GetCompleteness(IReadOnlyCollection<string> missingFacts) =>
        missingFacts.Count == 0 ? "Complete" : "Incomplete";

    public static GitFrozenFacts Extract(string category, string externalId, JsonElement item)
    {
        DateTimeOffset? Timestamp(string kind)
        {
            if (category != "commits" || !item.TryGetProperty("commit", out var commit) || commit.ValueKind != JsonValueKind.Object ||
                !commit.TryGetProperty(kind, out var person) || person.ValueKind != JsonValueKind.Object ||
                !person.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.String)
                return null;
            return DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : null;
        }
        var actors = new List<GitFrozenActor>();
        foreach (var (property, role) in new[] { ("author", "author"), ("committer", "committer"), ("user", "actor"), ("actor", "actor") })
        {
            if (item.TryGetProperty(property, out var actor) && actor.ValueKind == JsonValueKind.Object &&
                actor.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var accountId) && accountId > 0)
                actors.Add(new(role, accountId.ToString(CultureInfo.InvariantCulture)));
        }
        // Current contract does not establish relationship coverage or timestamp quality.
        return new(1, category, externalId, Timestamp("author"), Timestamp("committer"), null, actors, "Incomplete");
    }
}
