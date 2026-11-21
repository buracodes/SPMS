using Application.CQRS.KpiActuals;
using Domain.Entities;
using Domain.Enums;

namespace Application.Tests.Kpis;

public class ActualRevisionHistoryChainTests
{
    [Fact]
    public void Build_UsesSupersedesLinksNotTimestamps_AndNumbersFromOldest()
    {
        // Deliberately invert both dates: the link direction, not a timestamp guess,
        // is authoritative for revision order.
        var oldest = Revision("oldest", supersedes: null, created: new DateTime(2030, 1, 1), asOf: new DateTime(2030, 2, 1));
        var middle = Revision("middle", oldest.Id, created: new DateTime(2020, 1, 1), asOf: new DateTime(2020, 2, 1));
        var newest = Revision("newest", middle.Id, created: new DateTime(2010, 1, 1), asOf: new DateTime(2010, 2, 1), isCurrent: true);
        oldest.IsCurrent = false;
        middle.IsCurrent = false;

        var chain = ActualRevisionHistory.Build(new[] { oldest, middle, newest });

        Assert.False(chain.HasIntegrityIssues);
        Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, chain.Revisions.Select(r => r.Value.Id));
        Assert.Equal(new[] { 3, 2, 1 }, chain.Revisions.Select(r => r.RevisionNumber));
    }

    [Fact]
    public void Build_MissingPredecessor_RetainsEveryAvailableRevision_AndFlagsIssue()
    {
        var root = Revision("root");
        root.IsCurrent = false;
        var orphanCorrection = Revision("orphan-correction", Guid.NewGuid(), isCurrent: true);

        var chain = ActualRevisionHistory.Build(new[] { root, orphanCorrection });

        Assert.True(chain.HasIntegrityIssues);
        Assert.Equal(2, chain.Revisions.Count);
        Assert.Contains(root.Id, chain.Revisions.Select(r => r.Value.Id));
        Assert.Contains(orphanCorrection.Id, chain.Revisions.Select(r => r.Value.Id));
        Assert.Equal(new[] { 2, 1 }, chain.Revisions.Select(r => r.RevisionNumber));
    }

    [Fact]
    public void Build_CyclicChain_RetainsEveryRevision_AndFlagsIssue()
    {
        var first = Revision("first", supersedes: null, isCurrent: false);
        var second = Revision("second", first.Id, isCurrent: true);
        // Corrupted data closes the loop; no timestamp inference may hide either row.
        first.SupersedesActualId = second.Id;

        var chain = ActualRevisionHistory.Build(new[] { first, second });

        Assert.True(chain.HasIntegrityIssues);
        Assert.Equal(2, chain.Revisions.Count);
        Assert.Contains(first.Id, chain.Revisions.Select(r => r.Value.Id));
        Assert.Contains(second.Id, chain.Revisions.Select(r => r.Value.Id));
    }

    [Fact]
    public void Build_BranchAndMultipleCurrentRows_RetainAllRows_AndFlagIssues()
    {
        var root = Revision("root", supersedes: null, isCurrent: true);
        var branchA = Revision("branch-a", root.Id, isCurrent: false);
        var branchB = Revision("branch-b", root.Id, isCurrent: true);

        var chain = ActualRevisionHistory.Build(new[] { root, branchA, branchB });

        Assert.True(chain.HasIntegrityIssues);
        Assert.Equal(3, chain.Revisions.Count);
        Assert.Equal(
            new[] { root.Id, branchA.Id, branchB.Id }.OrderBy(id => id),
            chain.Revisions.Select(r => r.Value.Id).OrderBy(id => id));
        Assert.Equal(new[] { 3, 2, 1 }, chain.Revisions.Select(r => r.RevisionNumber));
    }

    [Fact]
    public void Build_DuplicateIds_DeDuplicateWithoutLosingUniqueRevisions()
    {
        var id = Guid.NewGuid();
        // Older-looking dates on the obsolete row must not influence de-duplication.
        var duplicateOld = Revision("duplicate-old", supersedes: null, created: new DateTime(2031, 1, 1), isCurrent: false);
        var duplicateNew = Revision("duplicate-new", supersedes: null, created: new DateTime(2020, 1, 1), isCurrent: true);
        duplicateOld.Id = id;
        duplicateNew.Id = id;

        var chain = ActualRevisionHistory.Build(new[] { duplicateOld, duplicateNew });

        Assert.True(chain.HasIntegrityIssues);
        Assert.Single(chain.Revisions);
        Assert.Equal("duplicate-new", chain.Revisions[0].Value.Note);
    }

    [Fact]
    public void Build_EmptyResult_IsEmptyWithoutIntegrityIssue()
    {
        var chain = ActualRevisionHistory.Build(Array.Empty<KpiActualValue>());

        Assert.Empty(chain.Revisions);
        Assert.False(chain.HasIntegrityIssues);
    }

    private static KpiActualValue Revision(
        string note,
        Guid? supersedes = null,
        DateTime? created = null,
        DateTime? asOf = null,
        bool isCurrent = false)
    {
        return new KpiActualValue
        {
            Id = Guid.NewGuid(),
            KpiId = Guid.NewGuid(),
            FiscalPeriodId = Guid.NewGuid(),
            Value = 1,
            DataSource = KpiDataSource.Manual,
            AsOfDate = asOf ?? new DateTime(2024, 1, 1),
            CreatedDate = created ?? new DateTime(2024, 1, 1),
            Note = note,
            IsCurrent = isCurrent,
            SupersedesActualId = supersedes
        };
    }
}
