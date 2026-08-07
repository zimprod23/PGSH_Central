using FluentAssertions;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using Xunit;

namespace PGSH.Tests.Domain;

// A service keeps an append-only trail of chef tenures: assigning opens a tenure, replacing or
// removing closes the previous one — so a validated evaluation stays traceable to the chef in
// charge at the time, across years.
public class ServiceChefHistoryTests
{
    private static Employee Chef() =>
        new() { Id = Guid.NewGuid(), Email = "c@x", Position = Position.ServiceChef };

    private static Service ServiceWith(Employee chef)
    {
        var service = new Service { Id = 1, Name = "S", Description = "" };
        service.AddStaff(chef);
        return service;
    }

    [Fact]
    public void Assigning_a_chef_opens_a_single_open_tenure()
    {
        var chef = Chef();
        var service = ServiceWith(chef);

        service.AssignChef(chef).IsSuccess.Should().BeTrue();

        service.ChefHistory.Should().ContainSingle();
        var tenure = service.ChefHistory.Single();
        tenure.EmployeeId.Should().Be(chef.Id);
        tenure.EndDate.Should().BeNull("the tenure is still open");
    }

    [Fact]
    public void Reassigning_closes_the_previous_tenure_and_opens_a_new_one()
    {
        var first = Chef();
        var second = Chef();
        var service = ServiceWith(first);
        service.AssignChef(first);

        service.AddStaff(second);
        service.AssignChef(second).IsSuccess.Should().BeTrue();

        service.ChefHistory.Should().HaveCount(2);
        service.ChefHistory.Single(h => h.EmployeeId == first.Id).EndDate.Should().NotBeNull();
        service.ChefHistory.Single(h => h.EmployeeId == second.Id).EndDate.Should().BeNull();
        service.ServiceChefId.Should().Be(second.Id);
    }

    [Fact]
    public void Reassigning_the_same_chef_does_not_open_a_second_tenure()
    {
        var chef = Chef();
        var service = ServiceWith(chef);
        service.AssignChef(chef);

        service.AssignChef(chef).IsSuccess.Should().BeTrue();

        service.ChefHistory.Should().ContainSingle();
    }

    [Fact]
    public void Removing_the_chef_closes_the_open_tenure()
    {
        var chef = Chef();
        var service = ServiceWith(chef);
        service.AssignChef(chef);

        service.RemoveChef();

        service.ServiceChefId.Should().BeNull();
        service.ChefHistory.Single().EndDate.Should().NotBeNull();
    }
}
