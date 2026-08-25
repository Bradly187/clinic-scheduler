# Privacy Policy

**Last Updated:** [DATE]

## Overview

ClinicScheduler is committed to protecting the privacy and security of your personal health information. This notice describes how medical information about you may be used and disclosed, and how you can get access to this information.

## Information We Collect

- **Personal Information:** Name, date of birth, email address, phone number, mailing address
- **Health Information:** Appointment history, treatment plans, therapy types, clinical notes, diagnosis codes, procedure codes
- **Financial Information:** Insurance policy details, billing addresses, payment transaction records (card details are never stored — processed through Stripe's PCI-compliant infrastructure)
- **Usage Information:** Login timestamps, access logs for security and audit purposes

## How We Use Your Information

- Schedule and manage appointments
- Communicate reminders and notifications about your care
- Generate invoices and process payments
- Submit insurance claims and superbills on your behalf
- Coordinate care with your healthcare providers (via FHIR integration)
- Comply with legal and regulatory requirements
- Maintain audit trails for security and compliance

## How We Protect Your Information

- **Encryption at Rest:** Sensitive fields (phone, notes, clinical data) are encrypted using AES-256 via the ASP.NET Data Protection API
- **Encryption in Transit:** All communications use TLS 1.2 or higher; HSTS enforced with 1-year max-age
- **Access Controls:** Role-based access (Admin, Clinic Manager, Staff, Therapist, Patient, Auditor) with principle of least privilege
- **Authentication:** Multi-factor authentication (TOTP) available for all accounts; account lockout after failed attempts
- **Audit Trail:** Every data access and modification is logged with user attribution and timestamps
- **Session Security:** 8-hour sliding expiration, HttpOnly cookies, rate-limited login endpoints

## Your Rights Under HIPAA

You have the right to:
- **Access:** Request a copy of your health information
- **Amend:** Request corrections to inaccurate health information
- **Accounting of Disclosures:** Request a list of certain disclosures of your information
- **Restrict:** Request restrictions on certain uses or disclosures
- **Confidential Communications:** Request that we communicate with you by alternative means or at alternative locations
- **Complaint:** File a complaint with us or the U.S. Department of Health and Human Services if you believe your privacy rights have been violated

## Disclosures We May Make Without Your Authorization

- For treatment, payment, and healthcare operations
- As required by law (court orders, public health reporting)
- To avert a serious threat to health or safety
- For health oversight activities
- For research purposes (with appropriate safeguards)

## Data Retention

- Patient health records: 6 years after last date of service (or longer if required by state law)
- Audit logs: 6 years
- Financial records: 7 years
- Account credentials: Until account deletion requested

## Third-Party Services

- **Stripe:** Payment processing (PCI DSS Level 1 certified)
- **Twilio:** SMS notifications (with documented patient consent per TCPA)
- **SMTP Provider:** Email communications
- **FHIR Server:** EHR interoperability (when configured by your clinic)

All third-party services are bound by their own privacy policies and, where applicable, Business Associate Agreements.

## Contact Information

For privacy-related questions or to exercise your rights:
- Email: [PRIVACY_EMAIL]
- Phone: [PRIVACY_PHONE]
- Mail: [CLINIC_ADDRESS]

## Changes to This Policy

We may update this policy periodically. We will notify you of material changes via email or in-app notification.
