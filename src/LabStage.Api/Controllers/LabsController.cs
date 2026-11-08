using System.Net;
using System.Text.Json;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

[ApiController]
public sealed class LabsController(LabStageDbContext db, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("api/v1/labs")]
    public async Task<IActionResult> SearchLabs(
        [FromQuery] string? q,
        [FromQuery] string? gov,
        [FromQuery] string? service,
        [FromQuery] decimal? maxPrice,
        [FromQuery] decimal? minRating,
        [FromQuery] List<string>? stage,
        [FromQuery] bool? verifiedOnly,
        [FromQuery] bool? courierOnly,
        [FromQuery] string? sort,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var query = db.Laboratories
            .Include(l => l.Services)
            .Include(l => l.Campaigns)
            .Where(l => l.VerificationStatus != VerificationStatus.Suspended)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(gov))
        {
            var g = MapGovCode(gov.Trim());
            query = query.Where(l => l.Governorate == g || l.City.Contains(g));
        }

        if (verifiedOnly == true)
            query = query.Where(l => l.VerificationStatus == VerificationStatus.Verified);

        if (courierOnly == true)
            query = query.Where(l => l.OffersCourier);

        if (minRating.HasValue)
            query = query.Where(l => l.AverageRating >= minRating.Value);

        var labs = await query.ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            labs = labs.Where(l =>
                l.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                l.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                l.Services.Any(s => s.Name.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (!string.IsNullOrWhiteSpace(service))
        {
            var srv = service.Trim();
            labs = labs.Where(l => l.Services.Any(s => s.Name.Contains(srv, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (maxPrice.HasValue)
        {
            var maxPiastres = (long)(maxPrice.Value * 100m);
            labs = labs.Where(l => l.Services.Any(s => s.PricePiastres <= maxPiastres)).ToList();
        }

        if (stage is { Count: > 0 })
        {
            labs = labs.Where(l => stage.All(reqStage =>
                l.Services.Any(s => s.SupportedStagesCsv.Contains(reqStage, StringComparison.OrdinalIgnoreCase)))).ToList();
        }

        // Increment impressions on active sponsored campaigns
        foreach (var c in labs.SelectMany(l => l.Campaigns).Where(c => c.IsActive && c.StartsAt <= now && c.EndsAt >= now))
        {
            c.Impressions++;
        }
        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        var items = labs.Select(l =>
        {
            var activeSponsored = l.Campaigns.Any(c => c.IsActive && c.StartsAt <= now && c.EndsAt >= now);
            var dist = (lat.HasValue && lng.HasValue && l.Latitude.HasValue && l.Longitude.HasValue)
                ? HaversineKm(lat.Value, lng.Value, l.Latitude.Value, l.Longitude.Value)
                : (double?)null;

            return new
            {
                l.Id,
                l.Slug,
                l.Name,
                l.Governorate,
                l.City,
                l.Description,
                IsVerified = l.VerificationStatus == VerificationStatus.Verified,
                IsSponsored = activeSponsored,
                l.OffersPickup,
                l.OffersCourier,
                l.AverageRating,
                l.ReviewCount,
                DistanceKm = dist,
                MinPriceEgp = l.Services.Count > 0 ? l.Services.Min(s => s.PricePiastres) / 100m : 0m,
                Services = l.Services.Where(s => s.IsActive).Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Category,
                    PriceEgp = s.PricePiastres / 100m,
                    s.EstimatedDays,
                    Stages = s.SupportedStagesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
                }).ToList(),
            };
        }).ToList();

        items = sort switch
        {
            "rating" => items.OrderByDescending(i => i.AverageRating).ThenByDescending(i => i.ReviewCount).ToList(),
            "priceAsc" => items.OrderBy(i => i.MinPriceEgp).ToList(),
            "distance" => items.OrderBy(i => i.DistanceKm ?? 99999).ToList(),
            _ => items.OrderByDescending(i => i.IsSponsored).ThenByDescending(i => i.IsVerified).ThenByDescending(i => i.AverageRating).ToList(),
        };

        return Ok(new { total = items.Count, items });
    }

    [HttpGet("api/v1/labs/{slug}")]
    public async Task<IActionResult> GetLabBySlug([FromRoute] string slug, CancellationToken ct)
    {
        var lab = await db.Laboratories
            .Include(l => l.Services)
            .Include(l => l.Equipment)
            .Include(l => l.Portfolio)
            .Include(l => l.Reviews)
            .Include(l => l.Campaigns)
            .FirstOrDefaultAsync(l => l.Slug == slug, ct);

        if (lab is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        foreach (var c in lab.Campaigns.Where(c => c.IsActive && c.StartsAt <= now && c.EndsAt >= now))
        {
            c.Clicks++;
        }
        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        return Ok(new
        {
            lab.Id,
            lab.OwnerUserId,
            lab.Slug,
            lab.Name,
            lab.Description,
            lab.Governorate,
            lab.City,
            lab.Address,
            ServiceAreas = lab.ServiceAreasCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
            lab.WorkingHours,
            IsVerified = lab.VerificationStatus == VerificationStatus.Verified,
            lab.OffersPickup,
            lab.OffersCourier,
            lab.AverageRating,
            lab.ReviewCount,
            Services = lab.Services.Where(s => s.IsActive).Select(s => new
            {
                s.Id,
                s.Category,
                s.Name,
                s.Description,
                PriceEgp = s.PricePiastres / 100m,
                s.EstimatedDays,
                Stages = s.SupportedStagesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
            }),
            Equipment = lab.Equipment.Select(e => new { e.Id, e.Name, e.BrandAndModel }),
            Portfolio = lab.Portfolio.Select(p => new { p.Id, p.Title, p.Caption, p.Shade }),
            Reviews = lab.Reviews.OrderByDescending(r => r.CreatedAt).Select(r => new
            {
                r.Id,
                r.DentistName,
                r.Rating,
                r.Comment,
                r.CreatedAt,
            }),
        });
    }

    public sealed record UpdateLabProfileRequest(
        string Name,
        string Description,
        string Governorate,
        string City,
        string Address,
        string ServiceAreasCsv,
        string WorkingHours,
        bool OffersPickup,
        bool OffersCourier);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPut("api/v1/labs/my/profile")]
    public async Task<IActionResult> UpdateMyLabProfile([FromBody] UpdateLabProfileRequest req, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) lab.Name = req.Name.Trim();
        if (req.Description is not null) lab.Description = req.Description.Trim();
        if (!string.IsNullOrWhiteSpace(req.Governorate)) lab.Governorate = req.Governorate.Trim();
        if (!string.IsNullOrWhiteSpace(req.City)) lab.City = req.City.Trim();
        if (req.Address is not null) lab.Address = req.Address.Trim();
        if (req.ServiceAreasCsv is not null) lab.ServiceAreasCsv = req.ServiceAreasCsv.Trim();
        if (!string.IsNullOrWhiteSpace(req.WorkingHours)) lab.WorkingHours = req.WorkingHours.Trim();
        lab.OffersPickup = req.OffersPickup;
        lab.OffersCourier = req.OffersCourier;

        await db.SaveChangesAsync(ct);
        return await GetLabBySlug(lab.Slug, ct);
    }

    public sealed record AddEquipmentRequest(string Name, string BrandAndModel);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPost("api/v1/labs/my/equipment")]
    public async Task<IActionResult> AddEquipment([FromBody] AddEquipmentRequest req, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name))
            return Problem("اسم الجهاز مطلوب.", statusCode: StatusCodes.Status400BadRequest);

        var eq = new LaboratoryEquipment
        {
            LaboratoryId = lab.Id,
            Name = req.Name.Trim(),
            BrandAndModel = string.IsNullOrWhiteSpace(req.BrandAndModel) ? "CAD/CAM Digital Unit" : req.BrandAndModel.Trim(),
        };
        db.LaboratoryEquipment.Add(eq);
        await db.SaveChangesAsync(ct);
        return Ok(new { eq.Id, eq.Name, eq.BrandAndModel });
    }

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpDelete("api/v1/labs/my/equipment/{id:guid}")]
    public async Task<IActionResult> DeleteEquipment([FromRoute] Guid id, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        var eq = await db.LaboratoryEquipment.FirstOrDefaultAsync(e => e.Id == id && e.LaboratoryId == lab.Id, ct);
        if (eq is null) return NotFound();
        db.LaboratoryEquipment.Remove(eq);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record AddPortfolioRequest(string Title, string Caption, string Shade);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPost("api/v1/labs/my/portfolio")]
    public async Task<IActionResult> AddPortfolio([FromBody] AddPortfolioRequest req, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Title))
            return Problem("عنوان العمل مطلوب.", statusCode: StatusCodes.Status400BadRequest);

        var item = new LaboratoryPortfolioImage
        {
            LaboratoryId = lab.Id,
            Title = req.Title.Trim(),
            Caption = req.Caption?.Trim() ?? string.Empty,
            Shade = string.IsNullOrWhiteSpace(req.Shade) ? "A2" : req.Shade.Trim(),
        };
        db.LaboratoryPortfolioImages.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(new { item.Id, item.Title, item.Caption, item.Shade });
    }

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpDelete("api/v1/labs/my/portfolio/{id:guid}")]
    public async Task<IActionResult> DeletePortfolio([FromRoute] Guid id, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        var item = await db.LaboratoryPortfolioImages.FirstOrDefaultAsync(p => p.Id == id && p.LaboratoryId == lab.Id, ct);
        if (item is null) return NotFound();
        db.LaboratoryPortfolioImages.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpGet("api/v1/labs/my")]
    public async Task<IActionResult> GetMyLab(CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        return await GetLabBySlug(lab.Slug, ct);
    }

    public sealed record UpsertServiceRequest(string Category, string Name, string Description, decimal PriceEgp, int EstimatedDays, List<string> Stages);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPost("api/v1/labs/my/services")]
    public async Task<IActionResult> AddService([FromBody] UpsertServiceRequest req, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name) || req.PriceEgp <= 0)
            return Problem("اسم الخدمة والسعر مطلوبان.", statusCode: StatusCodes.Status400BadRequest);

        var srv = new LaboratoryService
        {
            LaboratoryId = lab.Id,
            Category = string.IsNullOrWhiteSpace(req.Category) ? "تركيبات ثابتة" : req.Category.Trim(),
            Name = req.Name.Trim(),
            Description = req.Description?.Trim() ?? string.Empty,
            PricePiastres = (long)(req.PriceEgp * 100m),
            EstimatedDays = Math.Max(1, req.EstimatedDays),
            SupportedStagesCsv = req.Stages is { Count: > 0 } ? string.Join(',', req.Stages) : "Design,Milling,Layering,Glazing",
        };
        db.LaboratoryServices.Add(srv);
        await db.SaveChangesAsync(ct);
        return Ok(new { srv.Id, srv.Category, srv.Name, PriceEgp = srv.PricePiastres / 100m, srv.EstimatedDays });
    }

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPut("api/v1/labs/my/services/{id:guid}")]
    public async Task<IActionResult> UpdateService([FromRoute] Guid id, [FromBody] UpsertServiceRequest req, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        var srv = await db.LaboratoryServices.FirstOrDefaultAsync(s => s.Id == id && s.LaboratoryId == lab.Id, ct);
        if (srv is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Category)) srv.Category = req.Category.Trim();
        if (!string.IsNullOrWhiteSpace(req.Name)) srv.Name = req.Name.Trim();
        if (req.PriceEgp > 0) srv.PricePiastres = (long)(req.PriceEgp * 100m);
        if (req.EstimatedDays > 0) srv.EstimatedDays = req.EstimatedDays;
        if (req.Description is not null) srv.Description = req.Description.Trim();
        if (req.Stages is { Count: > 0 }) srv.SupportedStagesCsv = string.Join(',', req.Stages);

        await db.SaveChangesAsync(ct);
        return Ok(new { srv.Id, srv.Category, srv.Name, PriceEgp = srv.PricePiastres / 100m, srv.EstimatedDays });
    }

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpDelete("api/v1/labs/my/services/{id:guid}")]
    public async Task<IActionResult> DeleteService([FromRoute] Guid id, CancellationToken ct)
    {
        var lab = await ResolveMyLabAsync(ct);
        if (lab is null) return NotFound();
        var srv = await db.LaboratoryServices.FirstOrDefaultAsync(s => s.Id == id && s.LaboratoryId == lab.Id, ct);
        if (srv is null) return NotFound();

        db.LaboratoryServices.Remove(srv);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record CreateReviewRequest(Guid OrderId, int Rating, string Comment);

    [Authorize(Roles = AppRoles.Dentist)]
    [HttpPost("api/v1/labs/{slug}/reviews")]
    public async Task<IActionResult> CreateReview([FromRoute] string slug, [FromBody] CreateReviewRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await db.Laboratories.Include(l => l.Reviews).FirstOrDefaultAsync(l => l.Slug == slug, ct);
        if (lab is null) return NotFound();

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == req.OrderId && o.DentistUserId == userId && o.LaboratoryId == lab.Id, ct);
        if (order is null || order.Status != OrderStatus.Completed)
            return Problem("لا يمكن تقييم المعمل إلا بعد إتمام طلب مكتمل معه.", statusCode: StatusCodes.Status400BadRequest);

        if (await db.Reviews.AnyAsync(r => r.OrderId == order.Id, ct))
            return Problem("تم تقييم هذا الطلب بالفعل.", statusCode: StatusCodes.Status409Conflict);

        var rating = Math.Clamp(req.Rating, 1, 5);
        var review = new Review
        {
            OrderId = order.Id,
            LaboratoryId = lab.Id,
            DentistUserId = userId,
            DentistName = User.GetFullName(),
            Rating = rating,
            Comment = req.Comment?.Trim() ?? string.Empty,
        };
        db.Reviews.Add(review);

        var totalRating = (lab.AverageRating * lab.ReviewCount) + rating;
        lab.ReviewCount++;
        lab.AverageRating = Math.Round(totalRating / lab.ReviewCount, 2);

        await db.SaveChangesAsync(ct);
        return Ok(new { review.Id, lab.AverageRating, lab.ReviewCount });
    }

    // Server-rendered SEO routes for /labs and /labs/{slug}
    [HttpGet("labs")]
    public IActionResult RenderLabsDirectoryPage()
    {
        var path = Path.Combine(env.WebRootPath, "labs.html");
        return PhysicalFile(path, "text/html; charset=utf-8");
    }

    [HttpGet("labs/{slug}")]
    public async Task<IActionResult> RenderLabProfilePage([FromRoute] string slug, CancellationToken ct)
    {
        var lab = await db.Laboratories
            .Include(l => l.Services)
            .Include(l => l.Equipment)
            .Include(l => l.Portfolio)
            .Include(l => l.Reviews)
            .FirstOrDefaultAsync(l => l.Slug == slug, ct);

        if (lab is null) return NotFound();

        var templatePath = Path.Combine(env.WebRootPath, "lab-profile.html");
        var html = await System.IO.File.ReadAllTextAsync(templatePath, ct);

        var eName = WebUtility.HtmlEncode(lab.Name);
        var eDesc = WebUtility.HtmlEncode(lab.Description);
        var eGov = WebUtility.HtmlEncode(lab.Governorate);
        var eAddr = WebUtility.HtmlEncode($"{lab.Governorate} — {lab.City}، تغطية: {lab.ServiceAreasCsv}");
        var words = lab.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.StartsWith("ال", StringComparison.Ordinal) && w.Length > 2 ? w[2..] : w)
            .ToArray();
        var c1 = words.Length > 0 ? words[0][0] : 'م';
        var c2 = words.Length > 1 ? words[1][0] : 'ع';
        var initials = WebUtility.HtmlEncode($"{c1}\u200C{c2}");

        html = html
            .Replace("معمل القاهرة الرقمي للتركيبات", eName)
            .Replace("cairo-digital-dental-lab", WebUtility.HtmlEncode(lab.Slug))
            .Replace("<span id=\"lab-gov-crumb\">القاهرة</span>", $"<span id=\"lab-gov-crumb\">{eGov}</span>")
            .Replace("id=\"lab-bio\">معمل أسنان رقمي متخصص في تركيبات الزركونيا والإيماكس ودعامات الزرعات بتقنية CAD/CAM خمسة محاور مع مراجعة تصميم رقمية قبل التفريز.", $"id=\"lab-bio\">{eDesc}")
            .Replace("id=\"lab-initials\" aria-hidden=\"true\">كد", $"id=\"lab-initials\" aria-hidden=\"true\">{initials}")
            .Replace("id=\"lab-rating\">4.9", $"id=\"lab-rating\">{lab.AverageRating:0.0}")
            .Replace("id=\"lab-review-count\">42", $"id=\"lab-review-count\">{lab.ReviewCount}")
            .Replace("id=\"lab-address\">القاهرة — مدينة نصر، تغطية: مدينة نصر، مصر الجديدة، التجمع الخامس، المعادي", $"id=\"lab-address\">{eAddr}");

        var schema = JsonSerializer.Serialize(new
        {
            @context = "https://schema.org",
            @type = "MedicalBusiness",
            name = lab.Name,
            description = lab.Description,
            address = new
            {
                @type = "PostalAddress",
                addressLocality = lab.City,
                addressRegion = lab.Governorate,
                addressCountry = "EG",
            },
            priceRange = "EGP",
            aggregateRating = lab.ReviewCount > 0 ? new
            {
                @type = "AggregateRating",
                ratingValue = lab.AverageRating,
                reviewCount = lab.ReviewCount,
            } : null,
        });

        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            "<script type=\"application/ld\\+json\" id=\"lab-schema\">[\\s\\S]*?</script>",
            $"<script type=\"application/ld+json\" id=\"lab-schema\">{schema}</script>");

        return Content(html, "text/html; charset=utf-8");
    }

    private async Task<Laboratory?> ResolveMyLabAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        return await db.Laboratories.FirstOrDefaultAsync(l => l.OwnerUserId == userId || l.Staff.Any(s => s.UserId == userId), ct);
    }

    private static string MapGovCode(string gov) => gov.ToLowerInvariant() switch
    {
        "cairo" => "القاهرة",
        "giza" => "الجيزة",
        "alexandria" => "الإسكندرية",
        "dakahlia" => "الدقهلية",
        "sharqia" => "الشرقية",
        "gharbia" => "الغربية",
        "qalyubia" => "القليوبية",
        "monufia" => "المنوفية",
        "asyut" => "أسيوط",
        _ => gov,
    };

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
