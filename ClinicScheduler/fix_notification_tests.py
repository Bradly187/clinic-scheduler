import re

path = r"C:\painpi\clinic-scheduler\ClinicScheduler\ClinicScheduler.Core.Tests\AppointmentNotificationTests.cs"
with open(path, "r", encoding="utf-8") as f:
    content = f.read()

replacements = {
    r'\[InlineData\(NotificationType\.RequestApproved,\s*2\)\]': '[InlineData(NotificationType.RequestApproved, 4)]',
    r'\[InlineData\(NotificationType\.RequestDenied,\s*3\)\]': '[InlineData(NotificationType.RequestDenied, 5)]',
    r'\[InlineData\(NotificationType\.SchedulingConflict,\s*4\)\]': '[InlineData(NotificationType.SchedulingConflict, 6)]',
    r'\[InlineData\(NotificationType\.AppointmentRescheduled,\s*5\)\]': '[InlineData(NotificationType.AppointmentRescheduled, 7)]',
    r'\[InlineData\(NotificationType\.CancellationRequested,\s*6\)\]': '[InlineData(NotificationType.CancellationRequested, 8)]',
    r'\[InlineData\(NotificationType\.CancellationApproved,\s*7\)\]': '[InlineData(NotificationType.CancellationApproved, 9)]',
    r'\[InlineData\(NotificationType\.CancellationDenied,\s*8\)\]': '[InlineData(NotificationType.CancellationDenied, 10)]',
    r'NotificationType_AppointmentCreated_HasOrdinal9': 'NotificationType_AppointmentCreated_HasOrdinal11',
    r'\(\(int\)NotificationType\.AppointmentCreated\)\.Should\(\)\.Be\(9\)': '((int)NotificationType.AppointmentCreated).Should().Be(11)',
    r'NotificationType_AppointmentUpdated_HasOrdinal10': 'NotificationType_AppointmentUpdated_HasOrdinal12',
    r'\(\(int\)NotificationType\.AppointmentUpdated\)\.Should\(\)\.Be\(10\)': '((int)NotificationType.AppointmentUpdated).Should().Be(12)',
    r'NotificationType_WaitlistFulfilled_HasOrdinal11': 'NotificationType_WaitlistFulfilled_HasOrdinal13',
    r'\(\(int\)NotificationType\.WaitlistFulfilled\)\.Should\(\)\.Be\(11\)': '((int)NotificationType.WaitlistFulfilled).Should().Be(13)',
    r'NotificationType_HasExactly12Values': 'NotificationType_HasExactly14Values',
    r'\.HaveCount\(12\)': '.HaveCount(14)'
}

for old, new in replacements.items():
    content = re.sub(old, new, content)

with open(path, "w", encoding="utf-8") as f:
    f.write(content)
