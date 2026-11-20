using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Messaging;

namespace UniNet.Infrastructure.Data;

public sealed partial class UniNetDbContext
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<AccountBlock> AccountBlocks => Set<AccountBlock>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<ChatDeviceSession> ChatDeviceSessions => Set<ChatDeviceSession>();

    private static void ConfigureMessaging(ModelBuilder model)
    {
        model.Entity<Conversation>(e => {
            e.ToTable("Conversations", t => {
                t.HasCheckConstraint("CK_Conversations_Type", "\"Type\" IN (0,1)");
                t.HasCheckConstraint("CK_Conversations_DirectPair", "(\"Type\" = 0 AND \"DirectAccountLowId\" IS NOT NULL AND \"DirectAccountHighId\" IS NOT NULL AND \"DirectAccountLowId\" < \"DirectAccountHighId\" AND \"ProjectId\" IS NULL) OR (\"Type\" = 1 AND \"DirectAccountLowId\" IS NULL AND \"DirectAccountHighId\" IS NULL)");
            });
            e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.AvatarUrl).HasMaxLength(2048);
            e.HasIndex(x => new { x.DirectAccountLowId, x.DirectAccountHighId }).IsUnique().HasFilter("\"Type\" = 0");
            e.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"ProjectId\" IS NOT NULL");
            e.HasIndex(x => x.LastMessageAt);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.DirectAccountLowId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.DirectAccountHighId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
        });
        model.Entity<ConversationMember>(e => {
            e.ToTable("ConversationMembers", t => t.HasCheckConstraint("CK_ConversationMembers_Role", "\"Role\" BETWEEN 0 AND 2"));
            e.HasKey(x => new { x.ConversationId, x.AccountId });
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => new { x.ConversationId, x.LastReadMessageId }).HasPrincipalKey(x => new { x.ConversationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.LeftAt });
            e.HasIndex(x => x.ConversationId).IsUnique().HasFilter("\"Role\" = 2 AND \"LeftAt\" IS NULL").HasDatabaseName("IX_ConversationMembers_ActiveOwner");
        });
        model.Entity<ChatMessage>(e => {
            e.ToTable("Messages", t => {
                t.HasCheckConstraint("CK_Messages_Type", "\"Type\" BETWEEN 0 AND 2");
                t.HasCheckConstraint("CK_Messages_Number", "\"Number\" > 0");
            });
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.ConversationId, x.Id });
            e.Property(x => x.Content).HasMaxLength(8000);
            e.HasIndex(x => new { x.ConversationId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.SenderAccountId, x.ClientMessageId }).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ConversationMember>().WithMany().HasForeignKey(x => new { x.ConversationId, x.SenderAccountId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => new { x.ConversationId, x.ReplyToMessageId }).HasPrincipalKey(x => new { x.ConversationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MessageAttachment>(e => {
            e.ToTable("MessageAttachments"); e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(255); e.Property(x => x.FileUrl).HasMaxLength(2048); e.Property(x => x.MimeType).HasMaxLength(150); e.Property(x => x.StorageKey).HasMaxLength(512);
            e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<MessageReaction>(e => {
            e.ToTable("MessageReactions"); e.HasKey(x => x.Id); e.Property(x => x.ReactionType).HasMaxLength(12);
            e.HasIndex(x => new { x.MessageId, x.AccountId }).IsUnique();
            e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AccountBlock>(e => {
            e.ToTable("AccountBlocks", t => t.HasCheckConstraint("CK_AccountBlocks_NotSelf", "\"AccountId\" <> \"BlockedAccountId\"")); e.HasKey(x => new { x.AccountId, x.BlockedAccountId });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.BlockedAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PushDevice>(e => {
            e.ToTable("PushDevices"); e.HasKey(x => x.Id); e.Property(x => x.DeviceId).HasMaxLength(100); e.Property(x => x.Token).HasMaxLength(2048); e.Property(x => x.Platform).HasMaxLength(12);
            e.HasIndex(x => new { x.AccountId, x.DeviceId }).IsUnique(); e.HasIndex(x => x.Token).IsUnique();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ChatDeviceSession>(e => {
            e.ToTable("ChatDeviceSessions"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasMaxLength(128); e.Property(x => x.DeviceId).HasMaxLength(100);
            e.HasIndex(x => new { x.AccountId, x.DeviceId }); e.HasIndex(x => x.UpdatedAt);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ActiveConversationId).OnDelete(DeleteBehavior.SetNull);
        });
        model.Entity<NotificationDelivery>(e => {
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PushDevice>().WithMany().HasForeignKey(x => x.PushDeviceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Reason, x.DeliveredAt, x.RetryAt });
        });
    }
}
