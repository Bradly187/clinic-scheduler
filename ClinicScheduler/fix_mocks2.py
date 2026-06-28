import re

path = r"C:\painpi\clinic-scheduler\ClinicScheduler\ClinicScheduler.Web.Tests\Unit\AppointmentSchedulingServiceTests.cs"
with open(path, "r", encoding="utf-8") as f:
    content = f.read()

# Setup therapistShiftRepo whenever timeSlotRepo is setup to return Array.Empty<TimeSlot>()
content = content.replace(
    ".ReturnsAsync(Array.Empty<TimeSlot>());",
    ".ReturnsAsync(Array.Empty<TimeSlot>());\n        therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TherapistShift>());"
)

# wait, some of those are inside SetupLocationDeps which ALREADY has therapistShiftRepo setup!
# So we need to deduplicate it if it got added twice.
content = content.replace(
    """        therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TherapistShift>());
        
        therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TherapistShift>());""",
    """        therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TherapistShift>());"""
)

with open(path, "w", encoding="utf-8") as f:
    f.write(content)
