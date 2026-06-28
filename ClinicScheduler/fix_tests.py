import re

path = r"C:\painpi\clinic-scheduler\ClinicScheduler\ClinicScheduler.Web.Tests\Unit\AppointmentSchedulingServiceTests.cs"
with open(path, "r", encoding="utf-8") as f:
    content = f.read()

content = re.sub(r'ValidateSlotForLocation\(([^,]+),\s*1\)', r'ValidateSlotForLocation(\1, 1, 1)', content)
content = re.sub(r'GetDailySlotsForRoomAsync\(1,\s*([^,]+),\s*CancellationToken.None\)', r'GetDailySlotsForRoomAsync(1, 1, \1, CancellationToken.None)', content)

old_build_sut = """    private static AppointmentSchedulingService BuildSut(
        Mock<IRepository<Appointment>> apptRepo,
        Mock<IRepository<Patient>> patientRepo,
        Mock<IRepository<Therapist>> therapistRepo,
        Mock<IRepository<Room>> roomRepo,
        Mock<IRepository<TimeSlot>> timeSlotRepo,
        Mock<IRepository<Location>> locationRepo) =>
        new(apptRepo.Object, patientRepo.Object, therapistRepo.Object, roomRepo.Object,
            timeSlotRepo.Object, locationRepo.Object);"""

new_build_sut = """    private static AppointmentSchedulingService BuildSut(
        Mock<IRepository<Appointment>> apptRepo,
        Mock<IRepository<Patient>> patientRepo,
        Mock<IRepository<Therapist>> therapistRepo,
        Mock<IRepository<Room>> roomRepo,
        Mock<IRepository<TimeSlot>> timeSlotRepo,
        Mock<IRepository<Location>> locationRepo) =>
        new(apptRepo.Object, patientRepo.Object, therapistRepo.Object, roomRepo.Object,
            timeSlotRepo.Object, locationRepo.Object, new Mock<IRepository<TherapistShift>>().Object);"""

content = content.replace(old_build_sut, new_build_sut)

with open(path, "w", encoding="utf-8") as f:
    f.write(content)
