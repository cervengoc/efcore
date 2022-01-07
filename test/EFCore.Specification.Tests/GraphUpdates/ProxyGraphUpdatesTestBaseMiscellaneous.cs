// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

// ReSharper disable InconsistentNaming
// ReSharper disable AccessToModifiedClosure
// ReSharper disable PossibleMultipleEnumeration
namespace Microsoft.EntityFrameworkCore
{
    public abstract partial class ProxyGraphUpdatesTestBase<TFixture>
        where TFixture : ProxyGraphUpdatesTestBase<TFixture>.ProxyGraphUpdatesFixtureBase, new()
    {
        [ConditionalFact]
        public virtual void Attempting_to_save_two_entity_cycle_with_lazy_loading_throws()
        {
            ExecuteWithStrategyInTransaction(
                context =>
                {
                    context.AddRange(
                        context.CreateProxy<Car>(
                            car =>
                            {
                                car.Owner = context.CreateProxy<Person>();
                                car.Id = Guid.NewGuid();
                            }),
                        context.CreateProxy<Car>(
                            car =>
                            {
                                car.Owner = context.CreateProxy<Person>();
                                car.Id = Guid.NewGuid();
                            }));

                    context.SaveChanges();
                },
                context =>
                {
                    var cars = context.Set<Car>().ToList();

                    (cars[1].Owner, cars[0].Owner) = (cars[0].Owner, cars[1].Owner);

                    cars[0].Owner.Vehicle = cars[0];
                    cars[1].Owner.Vehicle = cars[1];

                    var message = Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message;
                    Assert.StartsWith(CoreStrings.CircularDependency("").Substring(0, 30), message);
                });
        }

        [ConditionalFact]
        public virtual void Can_use_record_proxies_with_base_types_to_load_reference()
        {
            ExecuteWithStrategyInTransaction(
                context =>
                {
                    context.AddRange(
                        context.CreateProxy<RecordCar>(
                            car =>
                            {
                                car.Owner = context.CreateProxy<RecordPerson>();
                            }));

                    context.SaveChanges();
                },
                context =>
                {
                    var car = context.Set<RecordCar>().Single();
                    if (!DoesLazyLoading)
                    {
                        context.Entry(car).Reference(e => e.Owner).Load();
                    }

                    Assert.Equal(car.Owner.Id, car.OwnerId);
                    Assert.Same(car, car.Owner.Vehicles.Single());
                });
        }

        [ConditionalFact]
        public virtual void Can_use_record_proxies_with_base_types_to_load_collection()
        {
            ExecuteWithStrategyInTransaction(
                context =>
                {
                    context.AddRange(
                        context.CreateProxy<RecordCar>(
                            car =>
                            {
                                car.Owner = context.CreateProxy<RecordPerson>();
                            }));

                    context.SaveChanges();
                },
                context =>
                {
                    var owner = context.Set<RecordPerson>().Single();
                    if (!DoesLazyLoading)
                    {
                        context.Entry(owner).Collection(e => e.Vehicles).Load();
                    }

                    Assert.Equal(owner.Id, owner.Vehicles.Single().Id);
                    Assert.Same(owner, owner.Vehicles.Single().Owner);
                });
        }

        [ConditionalFact]
        public virtual void Avoid_nulling_shared_FK_property_when_deleting()
        {
            ExecuteWithStrategyInTransaction(
                context =>
                {
                    var root = context
                        .Set<SharedFkRoot>()
                        .Include(e => e.Parents)
                        .Include(e => e.Dependants)
                        .Single();

                    Assert.Equal(3, context.ChangeTracker.Entries().Count());

                    var dependent = root.Dependants.Single();
                    var parent = root.Parents.Single();

                    Assert.Same(root, dependent.Root);
                    Assert.Same(parent, dependent.Parent);
                    Assert.Same(root, parent.Root);
                    Assert.Same(dependent, parent.Dependant);

                    Assert.Equal(root.Id, dependent.RootId);
                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Equal(dependent.Id, parent.DependantId);

                    context.Remove(dependent);

                    Assert.Equal(3, context.ChangeTracker.Entries().Count());

                    Assert.Equal(EntityState.Unchanged, context.Entry(root).State);
                    Assert.Equal(EntityState.Modified, context.Entry(parent).State);
                    Assert.Equal(EntityState.Deleted, context.Entry(dependent).State);

                    Assert.Same(root, dependent.Root);
                    Assert.Same(parent, dependent.Parent);
                    Assert.Same(root, parent.Root);
                    Assert.Null(parent.Dependant);

                    Assert.Equal(root.Id, dependent.RootId);
                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Null(parent.DependantId);

                    context.SaveChanges();

                    Assert.Equal(2, context.ChangeTracker.Entries().Count());

                    Assert.Equal(EntityState.Unchanged, context.Entry(root).State);
                    Assert.Equal(EntityState.Unchanged, context.Entry(parent).State);
                    Assert.Equal(EntityState.Detached, context.Entry(dependent).State);

                    Assert.Same(root, dependent.Root);
                    Assert.Same(parent, dependent.Parent);
                    Assert.Same(root, parent.Root);
                    Assert.Null(parent.Dependant);

                    Assert.Equal(root.Id, dependent.RootId);
                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Null(parent.DependantId);
                },
                context =>
                {
                    var root = context
                        .Set<SharedFkRoot>()
                        .Include(e => e.Parents)
                        .Include(e => e.Dependants)
                        .Single();

                    Assert.Equal(2, context.ChangeTracker.Entries().Count());

                    Assert.Empty(root.Dependants);
                    var parent = root.Parents.Single();

                    Assert.Same(root, parent.Root);
                    Assert.Null(parent.Dependant);

                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Null(parent.DependantId);
                });
        }

        [ConditionalTheory]
        [InlineData(false)]
        [InlineData(true)]
        public virtual void Avoid_nulling_shared_FK_property_when_nulling_navigation(bool nullPrincipal)
        {
            ExecuteWithStrategyInTransaction(
                context =>
                {
                    var root = context
                        .Set<SharedFkRoot>()
                        .Include(e => e.Parents)
                        .Include(e => e.Dependants)
                        .Single();

                    Assert.Equal(3, context.ChangeTracker.Entries().Count());

                    var dependent = root.Dependants.Single();
                    var parent = root.Parents.Single();

                    Assert.Same(root, dependent.Root);
                    Assert.Same(parent, dependent.Parent);
                    Assert.Same(root, parent.Root);
                    Assert.Same(dependent, parent.Dependant);

                    Assert.Equal(root.Id, dependent.RootId);
                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Equal(dependent.Id, parent.DependantId);

                    if (nullPrincipal)
                    {
                        dependent.Parent = null;
                    }
                    else
                    {
                        parent.Dependant = null;
                    }

                    Assert.Equal(3, context.ChangeTracker.Entries().Count());

                    Assert.Equal(EntityState.Unchanged, context.Entry(root).State);
                    Assert.Equal(EntityState.Modified, context.Entry(parent).State);
                    Assert.Equal(EntityState.Unchanged, context.Entry(dependent).State);

                    Assert.Same(root, dependent.Root);
                    Assert.Null(dependent.Parent);
                    Assert.Same(root, parent.Root);
                    Assert.Null(parent.Dependant);

                    Assert.Equal(root.Id, dependent.RootId);
                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Null(parent.DependantId);

                    context.SaveChanges();

                    Assert.Equal(3, context.ChangeTracker.Entries().Count());

                    Assert.Same(root, dependent.Root);
                    Assert.Null(dependent.Parent);
                    Assert.Same(root, parent.Root);
                    Assert.Null(parent.Dependant);

                    Assert.Equal(root.Id, dependent.RootId);
                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Null(parent.DependantId);
                },
                context =>
                {
                    var root = context
                        .Set<SharedFkRoot>()
                        .Include(e => e.Parents)
                        .Include(e => e.Dependants)
                        .Single();

                    Assert.Equal(3, context.ChangeTracker.Entries().Count());

                    var dependent = root.Dependants.Single();
                    var parent = root.Parents.Single();

                    Assert.Same(root, dependent.Root);
                    Assert.Null(dependent.Parent);
                    Assert.Same(root, parent.Root);
                    Assert.Null(parent.Dependant);

                    Assert.Equal(root.Id, dependent.RootId);
                    Assert.Equal(root.Id, parent.RootId);
                    Assert.Null(parent.DependantId);
                });
        }

        [ConditionalFact]
        public virtual void No_fixup_to_Deleted_entities()
        {
            using var context = CreateContext();

            var root = LoadRoot(context);
            if (!DoesLazyLoading)
            {
                context.Entry(root).Collection(e => e.OptionalChildren).Load();
            }

            var existing = root.OptionalChildren.OrderBy(e => e.Id).First();

            existing.Parent = null;
            existing.ParentId = null;
            ((ICollection<Optional1>)root.OptionalChildren).Remove(existing);

            context.Entry(existing).State = EntityState.Deleted;

            var queried = context.Set<Optional1>().ToList();

            Assert.Null(existing.Parent);
            Assert.Null(existing.ParentId);
            Assert.Single(root.OptionalChildren);
            Assert.DoesNotContain(existing, root.OptionalChildren);

            Assert.Equal(2, queried.Count);
            Assert.Contains(existing, queried);
        }

        [ConditionalFact]
        public virtual void Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref()
        {
            ExecuteWithStrategyInTransaction(
                context =>
                {
                    var dependent = context.Set<BadOrder>().Single();

                    dependent.BadCustomerId = null;

                    var principal = context.Set<BadCustomer>().Single();

                    principal.Status++;

                    Assert.Null(dependent.BadCustomerId);
                    Assert.Null(dependent.BadCustomer);
                    Assert.Empty(principal.BadOrders);

                    context.SaveChanges();

                    Assert.Null(dependent.BadCustomerId);
                    Assert.Null(dependent.BadCustomer);
                    Assert.Empty(principal.BadOrders);
                },
                context =>
                {
                    var dependent = context.Set<BadOrder>().Single();
                    var principal = context.Set<BadCustomer>().Single();

                    Assert.Null(dependent.BadCustomerId);
                    Assert.Null(dependent.BadCustomer);
                    Assert.Empty(principal.BadOrders);
                });
        }
    }
}
