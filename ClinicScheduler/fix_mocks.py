import os
import re

# 1. WaitlistServiceTests.cs
path1 = r"C:\painpi\clinic-scheduler\ClinicScheduler\ClinicScheduler.Web.Tests\Unit\WaitlistServiceTests.cs"
with open(path1, "r", encoding="utf-8") as f:
    content1 = f.read()

old1 = """            var schedulingService = new AppointmentSchedulingService(
                apptRepo.Object, _patientRepo.Object, therapistRepo.Object, roomRepo.Object,
                timeSlotRepo.Object, locationRepo.Object, new Mock<IRepository<TherapistShift>>().Object);"""
new1 = """            var therapistShiftRepo = new Mock<IRepository<TherapistShift>>();
            therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TherapistShift>());

            var schedulingService = new AppointmentSchedulingService(
                apptRepo.Object, _patientRepo.Object, therapistRepo.Object, roomRepo.Object,
                timeSlotRepo.Object, locationRepo.Object, therapistShiftRepo.Object);"""
content1 = content1.replace(old1, new1)
with open(path1, "w", encoding="utf-8") as f: f.write(content1)

# 2. TreatmentPlanScheduleServiceTests.cs
path2 = r"C:\painpi\clinic-scheduler\ClinicScheduler\ClinicScheduler.Web.Tests\Unit\TreatmentPlanScheduleServiceTests.cs"
with open(path2, "r", encoding="utf-8") as f:
    content2 = f.read()

old2 = """            var schedulingService = new AppointmentSchedulingService(
                apptRepo.Object, patientRepo.Object, therapistRepo.Object, roomRepo.Object,
                timeSlotRepo.Object, locationRepo.Object, new Mock<IRepository<TherapistShift>>().Object);"""
new2 = """            var therapistShiftRepo = new Mock<IRepository<TherapistShift>>();
            therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TherapistShift>());

            var schedulingService = new AppointmentSchedulingService(
                apptRepo.Object, patientRepo.Object, therapistRepo.Object, roomRepo.Object,
                timeSlotRepo.Object, locationRepo.Object, therapistShiftRepo.Object);"""
content2 = content2.replace(old2, new2)
with open(path2, "w", encoding="utf-8") as f: f.write(content2)

# 3. Core Tests AppointmentSchedulingServiceTests.cs
path3 = r"C:\painpi\clinic-scheduler\ClinicScheduler\ClinicScheduler.Core.Tests\Services\AppointmentSchedulingServiceTests.cs"
with open(path3, "r", encoding="utf-8") as f:
    content3 = f.read()

old3 = """        _timeSlotRepo
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<TimeSlot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TimeSlot>());"""
new3 = """        _timeSlotRepo
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<TimeSlot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TimeSlot>());
        _therapistShiftRepo
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TherapistShift>());"""
content3 = content3.replace(old3, new3)
with open(path3, "w", encoding="utf-8") as f: f.write(content3)

# 4. Web Tests AppointmentSchedulingServiceTests.cs
path4 = r"C:\painpi\clinic-scheduler\ClinicScheduler\ClinicScheduler.Web.Tests\Unit\AppointmentSchedulingServiceTests.cs"
with open(path4, "r", encoding="utf-8") as f:
    content4 = f.read()

old4_buildmocks = """    private static (
        Mock<IRepository<Appointment>>,
        Mock<IRepository<Patient>>,
        Mock<IRepository<Therapist>>,
        Mock<IRepository<Room>>,
        Mock<IRepository<TimeSlot>>,
        Mock<IRepository<Location>>) BuildMocks() =>
        (new(), new(), new(), new(), new(), new());"""
new4_buildmocks = """    private static (
        Mock<IRepository<Appointment>>,
        Mock<IRepository<Patient>>,
        Mock<IRepository<Therapist>>,
        Mock<IRepository<Room>>,
        Mock<IRepository<TimeSlot>>,
        Mock<IRepository<Location>>,
        Mock<IRepository<TherapistShift>>) BuildMocks() =>
        (new(), new(), new(), new(), new(), new(), new());"""
content4 = content4.replace(old4_buildmocks, new4_buildmocks)

content4 = re.sub(r'var \(\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+)\s*\)\s*=\s*BuildMocks\(\);', r'var (\1, \2, \3, \4, \5, \6, therapistShiftRepo) = BuildMocks();', content4)
content4 = content4.replace(', therapistShiftRepo)', ', _)')
content4 = content4.replace(', timeSlotRepo, locationRepo, _)', ', timeSlotRepo, locationRepo, therapistShiftRepo)')

old4_buildsut = """    private static AppointmentSchedulingService BuildSut(
        Mock<IRepository<Appointment>> apptRepo,
        Mock<IRepository<Patient>> patientRepo,
        Mock<IRepository<Therapist>> therapistRepo,
        Mock<IRepository<Room>> roomRepo,
        Mock<IRepository<TimeSlot>> timeSlotRepo,
        Mock<IRepository<Location>> locationRepo) =>
        new(apptRepo.Object, patientRepo.Object, therapistRepo.Object, roomRepo.Object,
            timeSlotRepo.Object, locationRepo.Object, new Mock<IRepository<TherapistShift>>().Object);"""
new4_buildsut = """    private static AppointmentSchedulingService BuildSut(
        Mock<IRepository<Appointment>> apptRepo,
        Mock<IRepository<Patient>> patientRepo,
        Mock<IRepository<Therapist>> therapistRepo,
        Mock<IRepository<Room>> roomRepo,
        Mock<IRepository<TimeSlot>> timeSlotRepo,
        Mock<IRepository<Location>> locationRepo,
        Mock<IRepository<TherapistShift>> therapistShiftRepo) =>
        new(apptRepo.Object, patientRepo.Object, therapistRepo.Object, roomRepo.Object,
            timeSlotRepo.Object, locationRepo.Object, therapistShiftRepo.Object);"""
content4 = content4.replace(old4_buildsut, new4_buildsut)

content4 = re.sub(r'BuildSut\(apptRepo,\s*patientRepo,\s*therapistRepo,\s*roomRepo,\s*timeSlotRepo,\s*locationRepo\)', r'BuildSut(apptRepo, patientRepo, therapistRepo, roomRepo, timeSlotRepo, locationRepo, therapistShiftRepo)', content4)
content4 = re.sub(r'BuildSut\(new\(\),\s*new\(\),\s*new\(\),\s*new\(\),\s*timeSlotRepo,\s*locationRepo\)', r'BuildSut(new(), new(), new(), new(), timeSlotRepo, locationRepo, therapistShiftRepo)', content4)
content4 = re.sub(r'BuildSut\(new\(\),\s*new\(\),\s*new\(\),\s*roomRepo,\s*timeSlotRepo,\s*locationRepo\)', r'BuildSut(new(), new(), new(), roomRepo, timeSlotRepo, locationRepo, therapistShiftRepo)', content4)

old4_setuplocationdeps = """    private static void SetupLocationDeps(
        Mock<IRepository<TimeSlot>> timeSlotRepo,
        Mock<IRepository<Location>> locationRepo,
        Mock<IRepository<Room>> roomRepo)
    {
        // No configured time slots — fall back to default 8–5 weekday schedule
        timeSlotRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TimeSlot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TimeSlot>());"""
new4_setuplocationdeps = """    private static void SetupLocationDeps(
        Mock<IRepository<TimeSlot>> timeSlotRepo,
        Mock<IRepository<Location>> locationRepo,
        Mock<IRepository<Room>> roomRepo,
        Mock<IRepository<TherapistShift>> therapistShiftRepo)
    {
        // No configured time slots — fall back to default 8–5 weekday schedule
        timeSlotRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TimeSlot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TimeSlot>());
        
        therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TherapistShift>());"""
content4 = content4.replace(old4_setuplocationdeps, new4_setuplocationdeps)

content4 = content4.replace('SetupLocationDeps(timeSlotRepo, locationRepo, roomRepo);', 'SetupLocationDeps(timeSlotRepo, locationRepo, roomRepo, therapistShiftRepo);')

setup_sut_pattern = r'timeSlotRepo\.Setup\(r => r\.FindAsync\(It\.IsAny<Expression<Func<TimeSlot, bool>>>\(\), It\.IsAny<CancellationToken>\(\)\)\)\s*\n\s*\.ReturnsAsync\(Array\.Empty<TimeSlot>\(\)\);\s*\n\s*var sut = BuildSut\(([^)]*timeSlotRepo,\s*locationRepo)\);'
new_setup_sut = r"""timeSlotRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TimeSlot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TimeSlot>());
        therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TherapistShift>());
        var sut = BuildSut(\1, therapistShiftRepo);"""
content4 = re.sub(setup_sut_pattern, new_setup_sut, content4)

with open(path4, "w", encoding="utf-8") as f: f.write(content4)
