using BookingTracker.Application.Common.Behaviors;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Pure Application-layer logic (template rendering, deciding who to notify) -
        // no SMTP dependency here at all, see IEmailNotificationService.
        services.AddScoped<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<ICalendarInvitationGenerator, CalendarInvitationGenerator>();
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        services.AddScoped<IBookingReminderScheduler, BookingReminderScheduler>();

        return services;
    }
}
