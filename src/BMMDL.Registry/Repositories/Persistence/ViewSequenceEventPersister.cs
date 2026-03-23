using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.Registry.Entities.Normalized;
using BMMDL.Registry.Repositories.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Repositories.Persistence;

/// <summary>
/// Handles persistence of BmView, BmSequence, and BmEvent objects.
/// </summary>
internal sealed class ViewSequenceEventPersister
{
    private readonly RepositoryContext _ctx;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _viewAnnotationsByOwner;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _sequenceAnnotationsByOwner;
    private Dictionary<Guid, List<NormalizedAnnotation>>? _eventAnnotationsByOwner;

    public ViewSequenceEventPersister(RepositoryContext ctx)
    {
        _ctx = ctx;
    }

    // ============================================================
    // View Operations
    // ============================================================

    public async Task SaveViewAsync(BmView view, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(view.Namespace, ct);
        var existing = await _ctx.Db.Views.Include(v => v.Parameters).FirstOrDefaultAsync(v => v.QualifiedName == view.QualifiedName, ct);

        string? projFieldsJson = view.ProjectionFields.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(view.ProjectionFields.Select(f => new { f.FieldName, f.Alias }))
            : null;
        string? exclFieldsJson = view.ExcludedFields.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(view.ExcludedFields)
            : null;
        string? parsedSelectJson = view.ParsedSelect != null
            ? SelectStatementSerializer.SerializeParsedSelect(view.ParsedSelect)
            : null;

        if (existing != null)
        {
            await _ctx.Db.NormalizedAnnotations
                .Where(a => a.OwnerType == "view" && a.OwnerId == existing.Id)
                .ExecuteDeleteAsync(ct);

            existing.Name = view.Name;
            existing.SelectStatement = view.SelectStatement;
            existing.IsProjection = view.IsProjection;
            existing.ProjectionEntityName = view.ProjectionEntityName;
            existing.ProjectionFieldsJson = projFieldsJson;
            existing.ExcludedFieldsJson = exclFieldsJson;
            existing.ParsedSelectJson = parsedSelectJson;
            existing.Parameters.Clear();
        }
        else
        {
            existing = new ViewRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _ctx.TenantId,
                Name = view.Name,
                QualifiedName = view.QualifiedName,
                NamespaceId = ns?.Id,
                SelectStatement = view.SelectStatement,
                IsProjection = view.IsProjection,
                ProjectionEntityName = view.ProjectionEntityName,
                ProjectionFieldsJson = projFieldsJson,
                ExcludedFieldsJson = exclFieldsJson,
                ParsedSelectJson = parsedSelectJson
            };
            _ctx.Db.Views.Add(existing);
        }

        int pos = 0;
        foreach (var p in view.Parameters)
        {
            existing.Parameters.Add(new ViewParameter
            {
                Id = Guid.NewGuid(),
                ViewId = existing.Id,
                Name = p.Name,
                TypeString = p.Type,
                DefaultValue = p.DefaultValue?.ToString(),
                Position = pos++
            });
        }
        AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, view.Annotations, "view", existing.Id);
        await _ctx.Db.SaveChangesAsync(ct);
    }

    public async Task SaveViewsAsync(IEnumerable<BmView> views, CancellationToken ct = default)
    {
        foreach (var v in views) await SaveViewAsync(v, ct);
    }

    public async Task<List<ViewRecord>> LoadViewsAsync(CancellationToken ct)
    {
        var views = await _ctx.Db.Views
            .AsNoTracking()
            .Where(v => v.TenantId == _ctx.TenantId)
            .Include(v => v.Parameters)
            .Include(v => v.Namespace)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (views.Count > 0)
        {
            var viewIds = views.Select(v => v.Id).ToHashSet();
            var annotations = await _ctx.Db.NormalizedAnnotations
                .AsNoTracking()
                .Where(a => a.OwnerType == "view" && viewIds.Contains(a.OwnerId))
                .ToListAsync(ct);

            _viewAnnotationsByOwner = annotations.GroupBy(a => a.OwnerId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
        else
        {
            _viewAnnotationsByOwner = new Dictionary<Guid, List<NormalizedAnnotation>>();
        }

        return views;
    }

    public BmView MapToBmView(ViewRecord record)
    {
        var view = new BmView
        {
            Name = record.Name,
            Namespace = record.Namespace?.Name ?? "",
            SelectStatement = record.SelectStatement,
            IsProjection = record.IsProjection,
            ProjectionEntityName = record.ProjectionEntityName
        };

        if (!string.IsNullOrEmpty(record.ProjectionFieldsJson))
        {
            var fields = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(record.ProjectionFieldsJson);
            if (fields != null)
            {
                foreach (var f in fields)
                {
                    view.ProjectionFields.Add(new BmProjectionField
                    {
                        FieldName = f.GetProperty("FieldName").GetString() ?? "",
                        Alias = f.TryGetProperty("Alias", out var alias) && alias.ValueKind != System.Text.Json.JsonValueKind.Null
                            ? alias.GetString() : null
                    });
                }
            }
        }

        if (!string.IsNullOrEmpty(record.ExcludedFieldsJson))
        {
            var excluded = System.Text.Json.JsonSerializer.Deserialize<List<string>>(record.ExcludedFieldsJson);
            if (excluded != null)
            {
                foreach (var field in excluded)
                    view.ExcludedFields.Add(field);
            }
        }

        if (!string.IsNullOrEmpty(record.ParsedSelectJson))
        {
            view.ParsedSelect = SelectStatementSerializer.DeserializeParsedSelect(record.ParsedSelectJson);
        }

        foreach (var p in record.Parameters.OrderBy(x => x.Position))
        {
            view.Parameters.Add(new BmViewParameter
            {
                Name = p.Name,
                Type = p.TypeString,
                DefaultValue = p.DefaultValue
            });
        }

        if (_viewAnnotationsByOwner != null
            && _viewAnnotationsByOwner.TryGetValue(record.Id, out var viewAnnotations))
        {
            foreach (var ann in AnnotationHelper.ReconstructAnnotations(viewAnnotations))
                view.Annotations.Add(ann);
        }

        return view;
    }

    // ============================================================
    // Sequence Operations
    // ============================================================

    public async Task SaveSequenceAsync(BmSequence sequence, CancellationToken ct = default)
    {
        var existing = await _ctx.Db.Sequences.FirstOrDefaultAsync(s => s.Name == sequence.Name, ct);
        if (existing != null)
        {
            await _ctx.Db.NormalizedAnnotations
                .Where(a => a.OwnerType == "sequence" && a.OwnerId == existing.Id)
                .ExecuteDeleteAsync(ct);

            existing.ForEntityName = sequence.ForEntity;
            existing.ForField = sequence.ForField;
            existing.Pattern = sequence.Pattern;
            existing.StartValue = sequence.StartValue;
            existing.Increment = sequence.Increment;
            existing.Padding = sequence.Padding;
            existing.MaxValue = sequence.MaxValue;
            existing.Scope = sequence.Scope.ToString().ToLower();
            existing.ResetOn = sequence.ResetOn.ToString().ToLower();
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, sequence.Annotations, "sequence", existing.Id);
        }
        else
        {
            var record = new SequenceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _ctx.TenantId,
                Name = sequence.Name,
                ForEntityName = sequence.ForEntity,
                ForField = sequence.ForField,
                Pattern = sequence.Pattern,
                StartValue = sequence.StartValue,
                Increment = sequence.Increment,
                Padding = sequence.Padding,
                MaxValue = sequence.MaxValue,
                Scope = sequence.Scope.ToString().ToLower(),
                ResetOn = sequence.ResetOn.ToString().ToLower()
            };
            _ctx.Db.Sequences.Add(record);
            AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, sequence.Annotations, "sequence", record.Id);
        }
        await _ctx.Db.SaveChangesAsync(ct);
    }

    public async Task SaveSequencesAsync(IEnumerable<BmSequence> sequences, CancellationToken ct = default)
    {
        foreach (var s in sequences) await SaveSequenceAsync(s, ct);
    }

    public async Task<List<SequenceRecord>> LoadSequencesAsync(CancellationToken ct)
    {
        var sequences = await _ctx.Db.Sequences
            .AsNoTracking()
            .Where(s => s.TenantId == _ctx.TenantId)
            .ToListAsync(ct);

        if (sequences.Count > 0)
        {
            var seqIds = sequences.Select(s => s.Id).ToHashSet();
            var annotations = await _ctx.Db.NormalizedAnnotations
                .AsNoTracking()
                .Where(a => a.OwnerType == "sequence" && seqIds.Contains(a.OwnerId))
                .ToListAsync(ct);

            _sequenceAnnotationsByOwner = annotations.GroupBy(a => a.OwnerId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
        else
        {
            _sequenceAnnotationsByOwner = new Dictionary<Guid, List<NormalizedAnnotation>>();
        }

        return sequences;
    }

    public BmSequence MapToBmSequence(SequenceRecord record)
    {
        var seq = new BmSequence
        {
            Name = record.Name,
            ForEntity = record.ForEntityName,
            ForField = record.ForField,
            Pattern = record.Pattern,
            StartValue = record.StartValue,
            Increment = record.Increment,
            Padding = record.Padding,
            MaxValue = record.MaxValue,
            Scope = Enum.TryParse<BmSequenceScope>(record.Scope, true, out var scope)
                ? scope : BmSequenceScope.Company,
            ResetOn = Enum.TryParse<BmResetTrigger>(record.ResetOn, true, out var reset)
                ? reset : BmResetTrigger.Never
        };

        if (_sequenceAnnotationsByOwner != null
            && _sequenceAnnotationsByOwner.TryGetValue(record.Id, out var seqAnnotations))
        {
            foreach (var ann in AnnotationHelper.ReconstructAnnotations(seqAnnotations))
                seq.Annotations.Add(ann);
        }

        return seq;
    }

    // ============================================================
    // Event Operations
    // ============================================================

    public async Task SaveEventAsync(BmEvent evt, CancellationToken ct = default)
    {
        var ns = await _ctx.GetOrCreateNamespaceAsync(evt.Namespace, ct);
        var existing = await _ctx.Db.Events.Include(e => e.Fields).FirstOrDefaultAsync(e => e.QualifiedName == evt.QualifiedName, ct);

        if (existing != null)
        {
            existing.Name = evt.Name;
            existing.Fields.Clear();

            await _ctx.Db.NormalizedAnnotations
                .Where(a => a.OwnerType == "event" && a.OwnerId == existing.Id)
                .ExecuteDeleteAsync(ct);
        }
        else
        {
            existing = new EventRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _ctx.TenantId,
                Name = evt.Name,
                QualifiedName = evt.QualifiedName,
                NamespaceId = ns?.Id
            };
            _ctx.Db.Events.Add(existing);
        }

        int pos = 0;
        foreach (var f in evt.Fields)
        {
            existing.Fields.Add(new EventField
            {
                Id = Guid.NewGuid(),
                EventId = existing.Id,
                Name = f.Name,
                TypeString = f.TypeString,
                Position = pos++
            });
        }
        AnnotationHelper.SaveAnnotationsForOwner(_ctx.Db, evt.Annotations, "event", existing.Id);
        await _ctx.Db.SaveChangesAsync(ct);
    }

    public async Task SaveEventsAsync(IEnumerable<BmEvent> events, CancellationToken ct = default)
    {
        foreach (var e in events) await SaveEventAsync(e, ct);
    }

    public async Task<List<EventRecord>> LoadEventsAsync(CancellationToken ct)
    {
        var events = await _ctx.Db.Events
            .AsNoTracking()
            .Where(e => e.TenantId == _ctx.TenantId)
            .Include(e => e.Fields)
            .Include(e => e.Namespace)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (events.Count > 0)
        {
            var eventIds = events.Select(e => e.Id).ToHashSet();
            var annotations = await _ctx.Db.NormalizedAnnotations
                .AsNoTracking()
                .Where(a => a.OwnerType == "event" && eventIds.Contains(a.OwnerId))
                .ToListAsync(ct);

            _eventAnnotationsByOwner = annotations.GroupBy(a => a.OwnerId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
        else
        {
            _eventAnnotationsByOwner = new Dictionary<Guid, List<NormalizedAnnotation>>();
        }

        return events;
    }

    public BmEvent MapToBmEvent(EventRecord record)
    {
        var evt = new BmEvent
        {
            Name = record.Name,
            Namespace = record.Namespace?.Name ?? ""
        };

        if (_eventAnnotationsByOwner != null
            && _eventAnnotationsByOwner.TryGetValue(record.Id, out var eventAnnotations))
        {
            foreach (var ann in AnnotationHelper.ReconstructAnnotations(eventAnnotations))
                evt.Annotations.Add(ann);
        }

        foreach (var f in record.Fields.OrderBy(x => x.Position))
        {
            evt.Fields.Add(new BmEventField
            {
                Name = f.Name,
                TypeString = f.TypeString
            });
        }

        return evt;
    }
}
