using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Ocsp;
using Rallyhub.Repository;
using Rallyhub.Repository.Entity;
using Rallyhub.Service.MailService;
using Exception = System.Exception;

namespace Rallyhub.Service.Customer;

public class Service : IService
{
    private readonly AppDbContext _dbContext;
    private readonly MediaService.IService _mediaService;
    private readonly IHttpContextAccessor _httpContext;
    private readonly Rallyhub.Service.MailService.IService _mailService;
    private readonly Rallyhub.Service.Notification.IService _notificationService;

    public Service(AppDbContext dbContext, MediaService.IService mediaService, IHttpContextAccessor httpContext, MailService.IService mailService, Rallyhub.Service.Notification.IService notificationService)
    {
        _dbContext = dbContext;
        _mediaService = mediaService;
        _httpContext = httpContext;
        _mailService = mailService;
        _notificationService = notificationService;
    }
    public async Task<string> OwnerRequest(Request.OwnerRequestRequest model)
    {
        var userId = _httpContext.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "UserId")?.Value;
        var userIdGuid = Guid.Parse(userId!);
        var user = _dbContext.Users.FirstOrDefault(x => x.Id == userIdGuid);
    
        // var customer = _dbContext.Customers.FirstOrDefault(x => x.UserId == userIdGuid);
        var customerId =_httpContext.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CustomerId")?.Value;
        var custmerIdGuild = Guid.Parse(customerId!);
        var customer = _dbContext.Customers.FirstOrDefault(x => x.Id == custmerIdGuild);
    
        if (user?.FirstName != model.FirstName || user?.LastName != model.LastName)
        {
            throw new Exception($"Tên {model.FirstName} {model.LastName} không khớp với tên của tài khoản này");
        }

        var isDuplicateIdentity = await _dbContext.OwnerRequests
            .AnyAsync(x => x.IdentityNumber == model.IdentityNumber && x.CustomerId != custmerIdGuild);
            
        if (isDuplicateIdentity)
        {
            throw new ArgumentException("Số CMND/CCCD đã được sử dụng bởi người dùng khác.");
        }
        var ownerRequest = new OwnerRequest()
        {
            BusinessName = model.BusinessName,
            TaxCode = model.TaxCode,
            BusinessAddress = model.BusinessAddress,
            BusinessLicenseUrl = await _mediaService.UploadImageAsync(model.BusinessLicenseUrl),
            IdentityNumber = model.IdentityNumber,
            IdentityCardFrontUrl = await _mediaService.UploadImageAsync(model.IdentityCardFrontUrl),
            IdentityCardBackUrl = await _mediaService.UploadImageAsync(model.IdentityCardBackUrl),
            CreatedAt = DateTimeOffset.UtcNow,
            CustomerId = custmerIdGuild,
            Status = "Pending",
        };
        _dbContext.OwnerRequests.Add(ownerRequest);
        _notificationService.CreateNotification(new Rallyhub.Service.Notification.Request.CreateNotificationRequest
        {
            UserId = userIdGuid,
            Title = "Có đơn đăng ký làm chủ sân mới",
            Content = $"Khách hàng {model.FirstName} {model.LastName} vừa nộp đơn xin trở thành chủ sân.",
            Type = Notification.Request.TypeNotification.OwnerRequestSubmitted,
            OwnerRequestId = ownerRequest.Id
        });

        var result = await _dbContext.SaveChangesAsync();
        if (result >= 1)
        {
            return "Success";
        }
        return "Fail";
    }
    public async Task<Base.Response.PageResult<Response.GetOwnerRequestResponse>> GetOwnerRequest(Base.Request.PagingRequest request)
    {
        if(request.PageIndex < 1)
            throw new Exception("PageIndex must be greater than or equal to 1");
        var customerId = _httpContext.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CustomerId")?.Value;
        if (customerId == null)
        {
            throw new Exception("Không tìm thấy thông tin của customer");
        }
        var customerIdGuild = Guid.Parse(customerId);
        
        var ownerRequestQuery = _dbContext.OwnerRequests
            .Where(x => x.CustomerId == customerIdGuild);
        
        var totalCount = await ownerRequestQuery.CountAsync();
        
        var items = await ownerRequestQuery
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new Response.GetOwnerRequestResponse()
            {
                Id =  x.Id,
                UserId = x.Customer.UserId,
                CustomerId = x.CustomerId,
                OwnerId = x.OwnerId,
                BusinessName = x.BusinessName,
                TaxCode = x.TaxCode,
                BusinessAddress = x.BusinessAddress,
                BusinessLicenseUrl = x.BusinessLicenseUrl,
                IdentityNumber = x.IdentityNumber,
                IdentityCardFrontUrl = x.IdentityCardFrontUrl,
                IdentityCardBackUrl = x.IdentityCardBackUrl,
                Status = x.Status,
                RejectionReason = x.RejectionReason,
                CreatedAt =  x.CreatedAt,
            })
            .ToListAsync();
        
        return new Base.Response.PageResult<Response.GetOwnerRequestResponse>()
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalCount,
        };
    }
    
   public async Task<Base.Response.PageResult<Response.LikeListResponse>> GetAllLikeList(Base.Request.PagingRequest request)
    {
        var getCustomerId = _httpContext.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CustomerId")?.Value;
        if (getCustomerId == null)
        {
            throw new Exception("Không xác minh được danh tính của Customer");
        }
        var customerId = Guid.Parse(getCustomerId!);
        
        var likeList = _dbContext.LikeListDetails
            .Include(x => x.Court)
            .Where(x => 
                x.CustomerId == customerId && 
                x.IsDeleted == false);
        var pageQuery = likeList
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize);
        var selectQuery = pageQuery.Select(x => new Response.LikeListResponse()
        {
            CourtId = x.CourtId,
            CourtName = x.Court.Name,
            CourtAddress = x.Court.Address,
            PictureUrl = x.Court.PictureUrl,
            Rating = _dbContext.Feedbacks.Where(y => y.CourtId == x.CourtId).Average(y => (double?)y.Rating)?? 5,
            Price = _dbContext.SubCourts
                .Where(y => y.CourtId == x.CourtId && y.IsDeleted == false)
                .SelectMany(y => y.ConfigSlots.Where(s => s.IsDeleted == false))
                .Min(s => (decimal?)s.Price) ?? 0
        });
        var listResult = await selectQuery.ToListAsync();
        return new Base.Response.PageResult<Response.LikeListResponse>()
        {
            Items = listResult,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = await likeList.CountAsync(),
        };
    }
    public async Task AddCourtLikeList(Request.AddCourtLikeListRequest request)
    {
        var getCustomerId = _httpContext.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CustomerId")?.Value;
        if (getCustomerId == null)
        {
            throw new Exception("Không xác minh được danh tính của Customer");
        }
        var customerId = Guid.Parse(getCustomerId);
        var court = await _dbContext.Courts
            .FirstOrDefaultAsync(x => x.Id == request.CourtId);
        if (court == null)
        {
            throw new Exception("Sân không tồn tại trên hệ thống");
        }
        var likeList = await _dbContext.LikeListDetails
            .FirstOrDefaultAsync(x => 
                x.CourtId == request.CourtId &&  
                x.CustomerId == customerId);
        if (likeList != null)
        {
            if (likeList.IsDeleted)
            {
                likeList.IsDeleted = false;
                likeList.UpdatedAt = DateTimeOffset.UtcNow;
                _dbContext.LikeListDetails.Update(likeList);
                await _dbContext.SaveChangesAsync();
                return;
            }
            throw new Exception("Đã tồn tại trong danh sách yêu thích");
        }
        var courtLike = new LikeListDetail()
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            CourtId = request.CourtId,
        };
        courtLike.CreatedAt = DateTimeOffset.UtcNow;
        await _dbContext.LikeListDetails.AddAsync(courtLike);
        await _dbContext.SaveChangesAsync();
    }
    public async Task DeleteCourtLikeList(Request.DeteleCourtLikeListRequest request)
    {
        var getCustomerId = _httpContext.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CustomerId")?.Value;
        var customerId = Guid.Parse(getCustomerId!);
        var courtLike = await _dbContext.LikeListDetails.FirstOrDefaultAsync(x => 
            x.CourtId == request.CourtId && x.CustomerId == customerId);
        if (courtLike == null)
        {
            throw new Exception("Sân không nằm trong danh sách yêu thích");
        }

        if (courtLike.IsDeleted)
        {
            throw new Exception("Sân không nằm trong danh sách yêu thích");
        }
        courtLike.IsDeleted = true;
        courtLike.UpdatedAt = DateTimeOffset.UtcNow;
        _dbContext.LikeListDetails.Update(courtLike);
        await _dbContext.SaveChangesAsync();
    }
    
}