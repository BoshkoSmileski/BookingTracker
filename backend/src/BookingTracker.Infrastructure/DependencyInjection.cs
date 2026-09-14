using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Infrastructure.Auth;
using BookingTracker.Infrastructure.BackgroundServices;
using BookingTracker.Infrastructure.Calendar;
using BookingTracker.Infrastructure.Email;
using BookingTracker.Infrastructure.Notifications;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.Infrastructure.RealTime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BookingTrackerDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IBookingTrackerDbContext>(provider => provider.GetRequiredService<BookingTrackerDbContext>());

        services.AddSignalR();
        services.AddScoped<IEventBroadcaster, SignalREventBroadcaster>();

        services.AddHostedService<BookingSessionAbandonmentSweeper>();
        services.AddHostedService<BookingReminderSweeper>();
        services.AddHostedService<EmailQueueProcessor>();

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        // Same "Email" section, a different (Application-owned) POCO for the queue's
        // retry policy - see EmailNotificationSettings for why it isn't just a couple
        // more properties on Infrastructure's EmailSettings.
        services.Configure<EmailNotificationSettings>(configuration.GetSection(EmailNotificationSettings.SectionName));
        services.Configure<ReminderSettings>(configuration.GetSection(ReminderSettings.SectionName));
        services.Configure<FrontendSettings>(configuration.GetSection(FrontendSettings.SectionName));
        services.AddSingleton<IFrontendLinkBuilder, FrontendLinkBuilder>();

        var emailSettings = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>();
        // Checked here, where the choice between the two senders is actually made,
        // so "SMTP is on but unconfigured" stops startup instead of surfacing one
        // failed notification at a time a quarter of an hour later.
        emailSettings?.EnsureValidForSending();

        if (emailSettings?.UseSmtp == true)
        {
            services.AddScoped<IEmailSender, SmtpEmailService>();
        }
        else
        {
            services.AddScoped<IEmailSender, FileSystemEmailService>();
        }

        // Keys must survive an app restart, or every previously-encrypted OAuth token
        // becomes permanently undecryptable the moment the process recycles - same
        // %TEMP%\BookingTracker convention already used for dev-mode sent emails.
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "BookingTracker", "dataprotection-keys")));

        services.Configure<GoogleCalendarSettings>(configuration.GetSection(GoogleCalendarSettings.SectionName));
        services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
        services.AddScoped<ICalendarConnectionService, CalendarConnectionService>();
        services.AddMemoryCache();
        services.AddScoped<ICalendarSyncService, CalendarSyncService>();
        services.AddHostedService<GoogleCalendarSyncSweeper>();

        return services;
    }
}
