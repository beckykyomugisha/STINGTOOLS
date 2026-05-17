// HC-22 — HealthcareHub SignalR hub.
// Provides real-time pressure-cascade and healthcare-event broadcast for
// the Planscape mobile app (pressure-live.tsx) and the BIM plugin twin.
//
// Mobile client connection: /hubs/healthcare
// Auth: JWT Bearer (same policy as ComplianceHub / NotificationHub)
// Groups: project-scoped via JoinProject / LeaveProject
//
// Events pushed to clients:
//   ReceivePressureReading  — live BACnet/OPC-UA pressure delta reading
//   ReceiveMgasAlarm        — MGPS zone alarm trigger
//   ReceiveAntiLigatureAlert — anti-ligature audit finding posted in real-time

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Planscape.Infrastructure.SignalR
{
    [Authorize]
    public class HealthcareHub : Hub
    {
        // ── Group helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Called by mobile clients after connecting. Subscribes the connection
        /// to project-scoped events for the given project ID.
        /// </summary>
        public async Task JoinProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return;
            await Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(projectId));
        }

        /// <summary>
        /// Removes the connection from project-scoped event delivery.
        /// Mobile clients call this before navigating away from the
        /// pressure-live screen.
        /// </summary>
        public async Task LeaveProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(projectId));
        }

        // ── Server→client broadcast helpers (called from controllers / background jobs) ─

        /// <summary>
        /// Broadcasts a live pressure-delta reading to all clients subscribed to
        /// <paramref name="projectId"/>. Called by the BACnet/OPC-UA twin bridge
        /// service and by <c>HealthcareController.PostPressureLog</c>.
        /// </summary>
        public static async Task BroadcastPressureReading(
            IHubContext<HealthcareHub> hubContext,
            string projectId,
            PressureReadingDto reading)
        {
            if (hubContext == null || string.IsNullOrWhiteSpace(projectId) || reading == null)
                return;

            reading.CapturedAt ??= DateTime.UtcNow.ToString("O");
            await hubContext.Clients
                .Group(ProjectGroup(projectId))
                .SendAsync("ReceivePressureReading", reading);
        }

        /// <summary>
        /// Broadcasts an MGPS zone alarm event to subscribed mobile clients.
        /// Called from <c>HealthcareController.PostMgasVerification</c> when
        /// <c>overallPass == false</c>.
        /// </summary>
        public static async Task BroadcastMgasAlarm(
            IHubContext<HealthcareHub> hubContext,
            string projectId,
            MgasAlarmDto alarm)
        {
            if (hubContext == null || string.IsNullOrWhiteSpace(projectId) || alarm == null)
                return;

            alarm.TriggeredAt ??= DateTime.UtcNow.ToString("O");
            await hubContext.Clients
                .Group(ProjectGroup(projectId))
                .SendAsync("ReceiveMgasAlarm", alarm);
        }

        /// <summary>
        /// Broadcasts an anti-ligature audit finding in real-time to subscribed
        /// clients. Called from <c>HealthcareController.PostAntiLigatureAudit</c>
        /// when <c>pass == false</c> (FAIL findings only, to minimise traffic).
        /// </summary>
        public static async Task BroadcastAntiLigatureAlert(
            IHubContext<HealthcareHub> hubContext,
            string projectId,
            AntiLigatureAlertDto alert)
        {
            if (hubContext == null || string.IsNullOrWhiteSpace(projectId) || alert == null)
                return;

            alert.AuditedAt ??= DateTime.UtcNow.ToString("O");
            await hubContext.Clients
                .Group(ProjectGroup(projectId))
                .SendAsync("ReceiveAntiLigatureAlert", alert);
        }

        // ── Private helpers ─────────────────────────────────────────────────────

        private static string ProjectGroup(string projectId) =>
            $"healthcare-project-{projectId}";
    }

    // ── DTOs (kept in the same file so the hub is self-contained) ─────────────

    /// <summary>Live pressure delta reading — mirrors the mobile RoomPressure type.</summary>
    public class PressureReadingDto
    {
        public string ProjectId    { get; set; }
        public string RoomId       { get; set; }
        public string RoomName     { get; set; }
        public string RoomClass    { get; set; }
        public string DesignRegime { get; set; }  // NEG | POS | NEUTRAL
        public int    DesignDeltaPa { get; set; }
        public double LiveDeltaPa  { get; set; }
        public bool   InBand       { get; set; }
        public string Source       { get; set; }  // BACNET | OPCUA | MANUAL
        public string CapturedAt   { get; set; }  // ISO 8601
    }

    /// <summary>MGPS zone alarm pushed to mobile on a FAIL verification.</summary>
    public class MgasAlarmDto
    {
        public string ProjectId   { get; set; }
        public string Zone        { get; set; }
        public string GasCode     { get; set; }
        public string FailReason  { get; set; }
        public string Severity    { get; set; }  // WARNING | CRITICAL
        public string TriggeredAt { get; set; }  // ISO 8601
    }

    /// <summary>Anti-ligature FAIL finding pushed to mobile in real-time.</summary>
    public class AntiLigatureAlertDto
    {
        public string ProjectId   { get; set; }
        public string RoomBimId   { get; set; }
        public string RoomName    { get; set; }
        public string FittingType { get; set; }
        public string Notes       { get; set; }
        public string AuditedBy   { get; set; }
        public string AuditedAt   { get; set; }  // ISO 8601
    }
}
