//using PGSH.Application.Abstractions.Authentication;
//using PGSH.Application.Abstractions.Data;
//using PGSH.Application.Abstractions.Messaging;
//using PGSH.Application.Users.GetById;
//using PGSH.Domain.Users;
//using PGSH.SharedKernel;

//namespace PGSH.Application.Students.GetById;


//internal sealed class GetStudentByIdQueryHandler(IApplicationDbContext context)
//    : IQueryHandler<GetStudentByIdQuery, StudentResponse>
//{
//    public async Task<Result<StudentResponse>> Handle(GetStudentByIdQuery query, CancellationToken cancellationToken)
//    {
//        if (query.UserId != userContext.UserId)
//        {
//            return Result.Failure<StudentResponse>(UserErrors.Unauthorized());
//        }

//        UserResponse? user = await context.Users
//            .Where(u => u.Id == query.UserId)
//            .Select(u => new UserResponse
//            {
//                Id = u.Id,
//                FirstName = u.FirstName,
//                LastName = u.LastName,
//                Email = u.Email
//            })
//            .SingleOrDefaultAsync(cancellationToken);

//        if (user is null)
//        {
//            return Result.Failure<UserResponse>(UserErrors.NotFound(query.UserId));
//        }

//        return user;
//    }
//}
