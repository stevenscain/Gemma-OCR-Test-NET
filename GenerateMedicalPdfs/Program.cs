using Bogus;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;
MedicalPdfGenerator.Run();

static class MedicalPdfGenerator
{
    static readonly Faker Fake = new();
    static readonly Random Rng = new();

    public static void Run()
    {
        var outputDir = Path.Combine("..", "test-pdfs");
        Directory.CreateDirectory(outputDir);
        var total = 0;

        void Generate(string docType, int count, bool multipage, Action<string, bool> gen)
        {
            for (int i = 1; i <= count; i++)
            {
                var filename = $"{docType}_{i:D2}.pdf";
                var filepath = Path.Combine(outputDir, filename);
                gen(filepath, multipage);
                Console.WriteLine($"Generated: {filepath}");
                total++;
            }
        }

        Generate("discharge_summary", 3, false, MakeDischargeSummary);
        Generate("lab_report", 3, false, MakeLabReport);
        Generate("prescription", 4, false, MakePrescription);
        Generate("discharge_summary_multi", 2, true, MakeDischargeSummary);
        Generate("lab_report_multi", 2, true, MakeLabReport);

        Console.WriteLine($"\nDone! Generated {total} synthetic medical PDFs in '{outputDir}/'");
    }

    // ── Patient / Vitals ─────────────────────────────────────────────────────

    static Dictionary<string, string> GeneratePatientInfo() => new()
    {
        ["name"] = Fake.Name.FullName(),
        ["dob"] = Fake.Date.Past(72, DateTime.Now.AddYears(-18)).ToString("MM/dd/yyyy"),
        ["ssn"] = Fake.Random.Replace("###-##-####"),
        ["address"] = Fake.Address.FullAddress(),
        ["phone"] = Fake.Phone.PhoneNumber(),
        ["mrn"] = $"MRN-{Fake.Random.Number(10000000, 99999999)}",
        ["insurance_id"] = $"INS-{Fake.Random.Long(1000000000, 9999999999)}",
    };

    static Dictionary<string, string> GenerateVitals() => new()
    {
        ["blood_pressure"] = $"{Rng.Next(100, 161)}/{Rng.Next(60, 101)} mmHg",
        ["heart_rate"] = $"{Rng.Next(55, 111)} bpm",
        ["temperature"] = $"{97.0 + Rng.NextDouble() * 4.5:F1} F",
        ["respiratory_rate"] = $"{Rng.Next(12, 25)} breaths/min",
        ["oxygen_saturation"] = $"{Rng.Next(92, 101)}%",
        ["weight"] = $"{Rng.Next(110, 281)} lbs",
        ["height"] = $"{Rng.Next(58, 77)} in",
    };

    // ── Clinical data ────────────────────────────────────────────────────────

    static readonly string[] Diagnoses =
    [
        "Essential Hypertension (I10)",
        "Type 2 Diabetes Mellitus (E11.9)",
        "Major Depressive Disorder, recurrent (F33.0)",
        "Acute Upper Respiratory Infection (J06.9)",
        "Chronic Obstructive Pulmonary Disease (J44.1)",
        "Gastroesophageal Reflux Disease (K21.0)",
        "Hyperlipidemia (E78.5)",
        "Osteoarthritis, unspecified (M19.90)",
        "Anxiety Disorder, unspecified (F41.9)",
        "Urinary Tract Infection (N39.0)",
        "Atrial Fibrillation (I48.91)",
        "Chronic Kidney Disease, Stage 3 (N18.3)",
    ];

    static readonly (string Name, string Dose, string Freq)[] Medications =
    [
        ("Metformin", "500mg", "twice daily"),
        ("Lisinopril", "10mg", "once daily"),
        ("Atorvastatin", "20mg", "once daily at bedtime"),
        ("Metoprolol", "25mg", "twice daily"),
        ("Omeprazole", "20mg", "once daily before breakfast"),
        ("Sertraline", "50mg", "once daily"),
        ("Amlodipine", "5mg", "once daily"),
        ("Levothyroxine", "75mcg", "once daily on empty stomach"),
        ("Gabapentin", "300mg", "three times daily"),
        ("Hydrochlorothiazide", "25mg", "once daily"),
        ("Prednisone", "10mg", "taper over 7 days"),
        ("Amoxicillin", "500mg", "three times daily for 10 days"),
    ];

    // ── Hospital course narratives ───────────────────────────────────────────

    static readonly string[] HospitalCourseSentences =
    [
        "Patient was admitted for acute management and close hemodynamic monitoring.",
        "On admission, the patient was alert and oriented, in mild-to-moderate distress.",
        "Initial assessment revealed tachycardia and elevated inflammatory markers.",
        "IV access was established and aggressive fluid resuscitation was initiated.",
        "The patient was placed on continuous telemetry and pulse oximetry.",
        "Cardiology was consulted and recommended rate control and anticoagulation therapy.",
        "Serial troponins were drawn and trended over the first 12 hours of admission.",
        "CT imaging of the chest and abdomen was obtained and reviewed with radiology.",
        "The patient was empirically started on broad-spectrum IV antibiotics pending culture results.",
        "Blood cultures, urine cultures, and respiratory cultures were obtained prior to antibiotic initiation.",
        "Nephrology was consulted for acute kidney injury with recommendations to hold nephrotoxic agents.",
        "The patient's electrolytes were repleted as needed with close monitoring of serum levels.",
        "Strict fluid balance was maintained with daily weight checks and accurate intake/output recording.",
        "Physical therapy and occupational therapy were consulted for early mobilization and functional assessment.",
        "The patient was maintained on a cardiac diet with a 2g sodium restriction.",
        "Insulin sliding scale was initiated for glycemic management with target glucose 140-180 mg/dL.",
        "Home medications were reviewed and held or adjusted as clinically indicated.",
        "The patient was transitioned from IV to oral antibiotics on hospital day three following clinical improvement.",
        "Repeat imaging demonstrated interval improvement with no new findings of concern.",
        "The patient remained afebrile and hemodynamically stable throughout the remainder of the hospitalization.",
        "Social work was involved to assist with discharge planning and community resource coordination.",
        "Respiratory therapy provided nebulization treatments every four hours with improvement in oxygen requirements.",
        "Vital signs trended toward baseline over the subsequent 48 hours.",
        "The patient demonstrated good understanding of the medication changes and dietary restrictions explained.",
        "By hospital day five, the patient met discharge criteria with stable vitals and tolerating oral intake.",
        "The patient was ambulating independently with supervision prior to discharge.",
        "Wound site was assessed daily with no signs of erythema, warmth, or purulent drainage.",
        "Echocardiogram demonstrated preserved ejection fraction with mild diastolic dysfunction.",
        "The patient's pain was adequately controlled with scheduled acetaminophen and PRN opioids.",
        "Case management coordinated a home health referral for post-discharge wound care and medication compliance.",
        "Pulmonology was consulted and recommended pulmonary function testing as an outpatient.",
        "DVT prophylaxis with subcutaneous heparin was maintained throughout the hospitalization.",
        "The patient was educated on fall prevention and instructed to use the call light as needed.",
        "Repeat laboratory values showed improvement in renal function and resolution of leukocytosis.",
        "Nutrition services were consulted for optimization of caloric intake and supplementation.",
        "The patient's family was present and actively involved in discharge planning discussions.",
    ];

    static readonly string[] HospitalCourseOpeners =
    [
        "Patient was admitted via the emergency department following {reason}.",
        "Patient presented with a {day}-day history of {symptom} and was admitted for further evaluation.",
        "Patient was transferred from an outside facility for higher level of care.",
        "Patient was admitted electively for management and optimization of chronic conditions.",
    ];

    static readonly string[] HospitalCourseReasons =
    [
        "acute decompensation of a known chronic condition",
        "new-onset chest pain with dyspnea",
        "fever, productive cough, and hypoxia",
        "acute onset confusion and altered mental status",
        "poorly controlled blood glucose and metabolic derangements",
        "hypertensive urgency with headache and visual changes",
        "abdominal pain with nausea and vomiting",
        "lower extremity swelling and exertional dyspnea",
    ];

    static readonly string[] HospitalCourseSymptoms =
    [
        "dyspnea and fatigue", "chest pain and palpitations", "fever and productive cough",
        "nausea, vomiting, and abdominal pain", "bilateral lower extremity edema",
    ];

    static string HospitalCourseParagraph()
    {
        var opener = Fake.PickRandom(HospitalCourseOpeners)
            .Replace("{reason}", Fake.PickRandom(HospitalCourseReasons))
            .Replace("{day}", Rng.Next(2, 8).ToString())
            .Replace("{symptom}", Fake.PickRandom(HospitalCourseSymptoms));
        var body = Fake.PickRandom(HospitalCourseSentences, Rng.Next(4, 8));
        return opener + " " + string.Join(" ", body);
    }

    // ── Consultation notes by specialty ──────────────────────────────────────

    static readonly Dictionary<string, string[]> ConsultNotes = new()
    {
        ["Cardiology"] =
        [
            "Patient evaluated for {indication}. Echocardiogram revealed preserved ejection fraction of {ef}% with mild diastolic dysfunction.",
            "Telemetry reviewed; rhythm consistent with {rhythm}. Rate control achieved with titration of beta-blockade.",
            "Recommend anticoagulation with apixaban given CHA2DS2-VASc score of {score}. Lipid panel reviewed; statin therapy optimized.",
            "Stress testing deferred given current clinical status; recommended as outpatient.",
            "Cardiac biomarkers trended downward over 12 hours, ruling out acute myocardial infarction by serial troponin protocol.",
            "Patient counseled on sodium restriction, daily weight monitoring, and signs of worsening heart failure.",
            "Follow-up in cardiology clinic in 2-4 weeks post-discharge with repeat echocardiogram at that visit.",
        ],
        ["Pulmonology"] =
        [
            "Patient assessed for hypoxic respiratory failure requiring supplemental oxygen at {o2} L/min via nasal cannula.",
            "Chest X-ray reviewed demonstrating bilateral infiltrates consistent with the clinical picture.",
            "Recommend continuation of broad-spectrum antibiotics and reassessment in 48 hours.",
            "Pulmonary function testing recommended as outpatient once acute illness resolves.",
            "Inhaled bronchodilators and corticosteroids initiated with good subjective response.",
            "Oxygen weaned to {o2_d} L/min by day of discharge; patient instructed on home oxygen use.",
            "Smoking cessation counseling provided; nicotine replacement therapy initiated.",
            "Sleep study referral placed for suspected obstructive sleep apnea given clinical presentation and BMI.",
        ],
        ["Infectious Disease"] =
        [
            "Patient evaluated for complicated infection requiring IV antibiotics.",
            "Culture sensitivities reviewed; antibiotic regimen narrowed to targeted coverage per susceptibilities.",
            "Recommend total antibiotic course of {days} days with transition to oral therapy once tolerating PO.",
            "Blood cultures finalized with no growth at 72 hours; IV antibiotics de-escalated accordingly.",
            "Source control assessed; no surgical intervention indicated at this time.",
            "Patient at risk for Clostridioides difficile; probiotics recommended and stool for C. diff sent.",
            "HIV and hepatitis serologies sent given clinical risk factors; results pending.",
            "Patient educated on medication adherence and signs and symptoms requiring return to care.",
        ],
        ["Nephrology"] =
        [
            "Patient assessed for acute kidney injury, likely {etiology} in etiology.",
            "Nephrotoxic medications identified and held; IV fluid challenge administered with urine output monitored.",
            "Creatinine trending downward following fluid resuscitation and removal of offending agents.",
            "Renal ultrasound obtained to rule out obstructive uropathy; results without significant abnormality.",
            "Electrolytes closely monitored; potassium supplementation adjusted per daily levels.",
            "Patient educated on the importance of adequate hydration and avoidance of NSAIDs.",
            "Outpatient nephrology follow-up arranged with repeat metabolic panel in one week.",
            "Chronic kidney disease staging reviewed; dietary counseling with nephrology dietitian recommended.",
        ],
        ["Endocrinology"] =
        [
            "Patient evaluated for suboptimal glycemic control with HbA1c elevated at {a1c}%.",
            "Insulin regimen reviewed and adjusted; basal-bolus strategy initiated with bedside glucose monitoring QID.",
            "Hypoglycemia protocol reviewed with nursing staff; glucagon kit ordered for the unit.",
            "Thyroid function tests obtained; results reviewed and levothyroxine dose adjusted.",
            "Patient and family counseled on carbohydrate counting, hypoglycemia recognition, and insulin administration technique.",
            "CGM initiation discussed with patient; referral placed for diabetes education program on discharge.",
            "Metformin held during hospitalization; to resume after discharge if renal function remains stable.",
            "A1c goal of <7% discussed with patient; medication compliance emphasized as key driver of glycemic control.",
        ],
    };

    static string ConsultationNote(string specialty)
    {
        var sentences = ConsultNotes.GetValueOrDefault(specialty, ConsultNotes["Cardiology"])!;
        var chosen = Fake.PickRandom(sentences, Math.Min(Rng.Next(4, 7), sentences.Length));
        var filled = chosen.Select(s => s
            .Replace("{indication}", Fake.PickRandom("atrial fibrillation", "chest pain evaluation", "heart failure management"))
            .Replace("{ef}", Rng.Next(45, 71).ToString())
            .Replace("{rhythm}", Fake.PickRandom("atrial fibrillation with controlled ventricular rate", "normal sinus rhythm with occasional PVCs"))
            .Replace("{score}", Rng.Next(2, 6).ToString())
            .Replace("{o2}", Rng.Next(2, 7).ToString())
            .Replace("{o2_d}", Rng.Next(1, 4).ToString())
            .Replace("{days}", Fake.PickRandom("7", "10", "14"))
            .Replace("{etiology}", Fake.PickRandom("prerenal", "intrinsic renal", "contrast-induced"))
            .Replace("{a1c}", (7.5 + Rng.NextDouble() * 4.0).ToString("F1")));
        return string.Join(" ", filled);
    }

    // ── Follow-up ────────────────────────────────────────────────────────────

    static readonly string[] FollowupSentences =
    [
        "All new and changed medications have been reviewed in detail with the patient and caregiver.",
        "The patient verbalized understanding of the signs and symptoms requiring immediate return to the emergency department.",
        "Dietary counseling was reinforced with written materials provided in the patient's preferred language.",
        "A medication reconciliation list was provided at discharge and reviewed with the patient.",
        "The patient is instructed to weigh themselves daily and call the office if weight increases by more than 3 lbs in 24 hours.",
        "Home blood pressure monitoring is recommended with log to be brought to follow-up appointment.",
        "The patient should avoid strenuous physical activity until cleared at the follow-up appointment.",
        "All follow-up laboratory work has been pre-ordered and scheduled at the outpatient facility.",
        "Patient instructed to continue wound care as demonstrated by nursing staff prior to discharge.",
        "Emergency contact information and after-hours nurse line number provided to patient and family.",
    ];

    static string FollowupParagraph(string doctor, string followUpDate)
    {
        var sentences = Fake.PickRandom(FollowupSentences, Rng.Next(2, 5));
        return $"Follow up with Dr. {doctor} on {followUpDate}. " + string.Join(" ", sentences);
    }

    // ── Clinical interpretation ──────────────────────────────────────────────

    static readonly string[] InterpSentences =
    [
        "The complete blood count demonstrates a leukocytosis consistent with an acute infectious or inflammatory process.",
        "Hemoglobin is below the lower limit of normal, consistent with mild normocytic anemia; further workup including iron studies and reticulocyte count is recommended.",
        "The comprehensive metabolic panel reveals an elevated creatinine and BUN consistent with acute kidney injury; trending recommended.",
        "Liver function tests are within normal limits with no evidence of hepatocellular injury or cholestasis.",
        "Electrolyte panel reveals hyponatremia; fluid restriction and sodium supplementation should be considered.",
        "The lipid panel demonstrates elevated LDL cholesterol above goal; intensification of statin therapy is indicated.",
        "Hemoglobin A1c is above target, suggesting suboptimal glycemic control over the preceding 3 months.",
        "TSH is suppressed with elevated free T4, consistent with primary hyperthyroidism; endocrinology referral recommended.",
        "Platelet count is within normal limits; no thrombocytopenia or thrombocytosis identified.",
        "Fasting glucose is elevated above the normal threshold; correlation with clinical history and repeat testing recommended.",
        "Potassium is at the lower limit of normal; oral supplementation and repeat level in 1-2 days advised.",
        "Albumin is mildly decreased, suggesting possible nutritional deficiency or protein-losing process; clinical correlation advised.",
        "Triglycerides are significantly elevated; lifestyle modification including dietary fat restriction and increased aerobic activity recommended.",
        "The coagulation profile is within normal limits; no evidence of coagulopathy identified on this panel.",
        "Creatinine has trended downward compared to prior values, suggesting improvement in renal function with treatment.",
        "Elevated AST and ALT warrant hepatology consultation and investigation for underlying hepatic pathology.",
        "These results have been reviewed by the laboratory director and are consistent with the clinical diagnosis.",
        "Critical values, if any, were communicated directly to the ordering provider per laboratory policy.",
    ];

    static string ClinicalInterpretationParagraph()
    {
        var sentences = Fake.PickRandom(InterpSentences, Rng.Next(3, 6));
        return string.Join(" ", sentences);
    }

    // ── Lab test definitions ─────────────────────────────────────────────────

    static readonly (string Name, Func<string> ValueFn, string RefRange)[] LabTests =
    [
        ("Glucose, Fasting", () => $"{Rng.Next(70, 251)} mg/dL", "70-100 mg/dL"),
        ("Hemoglobin A1c", () => $"{4.5 + Rng.NextDouble() * 7.5:F1}%", "4.0-5.6%"),
        ("Total Cholesterol", () => $"{Rng.Next(140, 301)} mg/dL", "<200 mg/dL"),
        ("LDL Cholesterol", () => $"{Rng.Next(50, 201)} mg/dL", "<100 mg/dL"),
        ("HDL Cholesterol", () => $"{Rng.Next(25, 91)} mg/dL", ">40 mg/dL"),
        ("Triglycerides", () => $"{Rng.Next(50, 401)} mg/dL", "<150 mg/dL"),
        ("Creatinine", () => $"{0.5 + Rng.NextDouble() * 3.0:F2} mg/dL", "0.7-1.3 mg/dL"),
        ("BUN", () => $"{Rng.Next(5, 46)} mg/dL", "7-20 mg/dL"),
        ("WBC", () => $"{3.0 + Rng.NextDouble() * 15.0:F1} K/uL", "4.5-11.0 K/uL"),
        ("Hemoglobin", () => $"{8.0 + Rng.NextDouble() * 10.0:F1} g/dL", "12.0-17.5 g/dL"),
        ("Platelet Count", () => $"{Rng.Next(100, 451)} K/uL", "150-400 K/uL"),
        ("TSH", () => $"{0.1 + Rng.NextDouble() * 9.9:F2} mIU/L", "0.4-4.0 mIU/L"),
        ("Sodium", () => $"{Rng.Next(130, 151)} mEq/L", "136-145 mEq/L"),
        ("Potassium", () => $"{2.8 + Rng.NextDouble() * 3.2:F1} mEq/L", "3.5-5.0 mEq/L"),
    ];

    static readonly (string PanelName, (string Name, Func<string> ValueFn, string RefRange)[] Labs)[] LabPanels =
    [
        ("COMPLETE BLOOD COUNT (CBC)", [
            ("WBC", () => $"{3.0 + Rng.NextDouble() * 15.0:F1} K/uL", "4.5-11.0 K/uL"),
            ("RBC", () => $"{3.5 + Rng.NextDouble() * 3.0:F2} M/uL", "4.5-5.5 M/uL"),
            ("Hemoglobin", () => $"{8.0 + Rng.NextDouble() * 10.0:F1} g/dL", "12.0-17.5 g/dL"),
            ("Hematocrit", () => $"{25.0 + Rng.NextDouble() * 30.0:F1}%", "36-46%"),
            ("MCV", () => $"{70.0 + Rng.NextDouble() * 40.0:F1} fL", "80-100 fL"),
            ("MCH", () => $"{25.0 + Rng.NextDouble() * 10.0:F1} pg", "27-33 pg"),
            ("MCHC", () => $"{30.0 + Rng.NextDouble() * 8.0:F1} g/dL", "32-36 g/dL"),
            ("Platelet Count", () => $"{Rng.Next(100, 451)} K/uL", "150-400 K/uL"),
            ("MPV", () => $"{7.0 + Rng.NextDouble() * 5.0:F1} fL", "7.5-11.5 fL"),
        ]),
        ("COMPREHENSIVE METABOLIC PANEL (CMP)", [
            ("Glucose, Fasting", () => $"{Rng.Next(70, 251)} mg/dL", "70-100 mg/dL"),
            ("BUN", () => $"{Rng.Next(5, 46)} mg/dL", "7-20 mg/dL"),
            ("Creatinine", () => $"{0.5 + Rng.NextDouble() * 3.0:F2} mg/dL", "0.7-1.3 mg/dL"),
            ("eGFR", () => $"{Rng.Next(15, 121)} mL/min", ">60 mL/min"),
            ("Sodium", () => $"{Rng.Next(130, 151)} mEq/L", "136-145 mEq/L"),
            ("Potassium", () => $"{2.8 + Rng.NextDouble() * 3.2:F1} mEq/L", "3.5-5.0 mEq/L"),
            ("Chloride", () => $"{Rng.Next(95, 111)} mEq/L", "98-106 mEq/L"),
            ("CO2", () => $"{Rng.Next(18, 33)} mEq/L", "23-29 mEq/L"),
            ("Calcium", () => $"{7.5 + Rng.NextDouble() * 3.5:F1} mg/dL", "8.5-10.5 mg/dL"),
            ("Total Protein", () => $"{5.0 + Rng.NextDouble() * 4.0:F1} g/dL", "6.0-8.3 g/dL"),
            ("Albumin", () => $"{2.5 + Rng.NextDouble() * 3.0:F1} g/dL", "3.5-5.5 g/dL"),
            ("Bilirubin, Total", () => $"{0.1 + Rng.NextDouble() * 2.9:F1} mg/dL", "0.1-1.2 mg/dL"),
            ("ALP", () => $"{Rng.Next(30, 201)} U/L", "44-147 U/L"),
            ("AST", () => $"{Rng.Next(10, 151)} U/L", "10-40 U/L"),
            ("ALT", () => $"{Rng.Next(7, 151)} U/L", "7-56 U/L"),
        ]),
        ("LIPID PANEL", [
            ("Total Cholesterol", () => $"{Rng.Next(140, 301)} mg/dL", "<200 mg/dL"),
            ("LDL Cholesterol", () => $"{Rng.Next(50, 201)} mg/dL", "<100 mg/dL"),
            ("HDL Cholesterol", () => $"{Rng.Next(25, 91)} mg/dL", ">40 mg/dL"),
            ("Triglycerides", () => $"{Rng.Next(50, 401)} mg/dL", "<150 mg/dL"),
            ("VLDL Cholesterol", () => $"{Rng.Next(5, 61)} mg/dL", "5-40 mg/dL"),
        ]),
        ("THYROID PANEL", [
            ("TSH", () => $"{0.1 + Rng.NextDouble() * 9.9:F2} mIU/L", "0.4-4.0 mIU/L"),
            ("Free T4", () => $"{0.5 + Rng.NextDouble() * 2.0:F2} ng/dL", "0.8-1.8 ng/dL"),
            ("Free T3", () => $"{1.5 + Rng.NextDouble() * 3.5:F1} pg/mL", "2.3-4.2 pg/mL"),
        ]),
        ("HEMOGLOBIN A1C", [
            ("Hemoglobin A1c", () => $"{4.5 + Rng.NextDouble() * 7.5:F1}%", "4.0-5.6%"),
            ("Estimated Avg Glucose", () => $"{Rng.Next(70, 301)} mg/dL", "70-126 mg/dL"),
        ]),
    ];

    // ── Document generators ──────────────────────────────────────────────────

    static void MakeDischargeSummary(string filepath, bool multipage)
    {
        var patient = GeneratePatientInfo();
        var vitals = GenerateVitals();
        var admitDate = Fake.Date.Past(0, DateTime.Now.AddDays(-2));
        var dischargeDate = admitDate.AddDays(Rng.Next(1, 15));
        var doctor = Fake.Name.FullName();
        var diagnoses = Fake.PickRandom(Diagnoses, multipage ? Rng.Next(4, 7) : Rng.Next(2, 6)).ToList();
        var meds = Fake.PickRandom(Medications, multipage ? Rng.Next(6, 11) : Rng.Next(3, 7)).ToList();

        var facilityName = Fake.Company.CompanyName() + " Medical Center";
        var facilityAddr = Fake.Address.FullAddress();
        var facilityPhone = Fake.Phone.PhoneNumber();
        var facilityFax = Fake.Phone.PhoneNumber();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginHorizontal(40);
                page.MarginVertical(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text(facilityName).Bold().FontSize(16);
                    col.Item().AlignCenter().Text(facilityAddr).FontSize(9);
                    col.Item().AlignCenter().Text($"Phone: {facilityPhone}  |  Fax: {facilityFax}").FontSize(9);
                    col.Item().PaddingVertical(4).LineHorizontal(1);
                    col.Item().PaddingTop(5);
                    col.Item().AlignCenter().Text("DISCHARGE SUMMARY").Bold().FontSize(14);
                    col.Item().PaddingTop(5);

                    col.Item().Text("PATIENT INFORMATION").Bold().FontSize(11);
                    col.Item().Row(r => { r.RelativeItem().Text($"Patient Name: {patient["name"]}"); r.RelativeItem().Text($"MRN: {patient["mrn"]}"); });
                    col.Item().Row(r => { r.RelativeItem().Text($"Date of Birth: {patient["dob"]}"); r.RelativeItem().Text($"Insurance ID: {patient["insurance_id"]}"); });
                    col.Item().Row(r => { r.RelativeItem().Text($"Admission Date: {admitDate:MM/dd/yyyy}"); r.RelativeItem().Text($"Discharge Date: {dischargeDate:MM/dd/yyyy}"); });
                    col.Item().Text($"Attending Physician: Dr. {doctor}");
                    col.Item().PaddingTop(5);

                    col.Item().Text("VITALS AT DISCHARGE").Bold().FontSize(11);
                    col.Item().Row(r => { r.RelativeItem().Text($"BP: {vitals["blood_pressure"]}"); r.RelativeItem().Text($"HR: {vitals["heart_rate"]}"); });
                    col.Item().Row(r => { r.RelativeItem().Text($"Temp: {vitals["temperature"]}"); r.RelativeItem().Text($"SpO2: {vitals["oxygen_saturation"]}"); });
                    col.Item().Row(r => { r.RelativeItem().Text($"Resp Rate: {vitals["respiratory_rate"]}"); r.RelativeItem().Text($"Weight: {vitals["weight"]}"); });
                    col.Item().PaddingTop(5);

                    col.Item().Text("DIAGNOSES").Bold().FontSize(11);
                    for (int i = 0; i < diagnoses.Count; i++)
                        col.Item().Text($"  {i + 1}. {diagnoses[i]}");
                    col.Item().PaddingTop(5);

                    col.Item().Text("HOSPITAL COURSE").Bold().FontSize(11);
                    var numParagraphs = multipage ? Rng.Next(4, 8) : 1;
                    for (int i = 0; i < numParagraphs; i++)
                    {
                        col.Item().Text(HospitalCourseParagraph());
                        col.Item().PaddingTop(3);
                    }
                    col.Item().PaddingTop(2);

                    col.Item().Text("DISCHARGE MEDICATIONS").Bold().FontSize(11);
                    for (int i = 0; i < meds.Count; i++)
                        col.Item().Text($"  {i + 1}. {meds[i].Name} {meds[i].Dose} - {meds[i].Freq}");
                    col.Item().PaddingTop(5);

                    if (multipage)
                    {
                        col.Item().Text("PROCEDURES PERFORMED").Bold().FontSize(11);
                        string[] procedures =
                        [
                            "Central line placement (right internal jugular)",
                            "CT scan of chest/abdomen/pelvis with IV contrast",
                            "Echocardiogram (transthoracic)",
                            "Bronchoscopy with lavage",
                            "Blood transfusion (2 units packed RBCs)",
                            "Lumbar puncture",
                            "Arterial blood gas analysis",
                        ];
                        var selectedProcs = Fake.PickRandom(procedures, Rng.Next(3, 6)).ToList();
                        for (int i = 0; i < selectedProcs.Count; i++)
                            col.Item().Text($"  {i + 1}. {selectedProcs[i]}");
                        col.Item().PaddingTop(5);

                        col.Item().Text("CONSULTATION NOTES").Bold().FontSize(11);
                        string[] specialties = ["Cardiology", "Pulmonology", "Infectious Disease", "Nephrology", "Endocrinology"];
                        var selectedSpecs = Fake.PickRandom(specialties, Rng.Next(2, 5)).ToList();
                        foreach (var spec in selectedSpecs)
                        {
                            col.Item().Text($"  {spec} - Dr. {Fake.Name.FullName()}").Bold();
                            col.Item().Text($"    {ConsultationNote(spec)}");
                            col.Item().PaddingTop(3);
                        }
                        col.Item().PaddingTop(5);

                        col.Item().Text("PATIENT EDUCATION & DISCHARGE INSTRUCTIONS").Bold().FontSize(11);
                        string[] instructions =
                        [
                            "Diet: Low sodium (<2g/day), diabetic diet as previously prescribed.",
                            "Activity: No heavy lifting >10 lbs for 2 weeks. Gradual return to normal activity.",
                            "Wound care: Keep incision site clean and dry. Change dressing daily.",
                            "Warning signs: Return to ER if experiencing chest pain, shortness of breath, fever >101.5F, or worsening symptoms.",
                            "Medications: Take all medications as prescribed. Do not skip doses.",
                            $"Follow-up labs: CBC, CMP, and coagulation panel in 1 week at {Fake.Company.CompanyName()} Lab.",
                            "Smoking cessation counseling provided. Patient advised to quit smoking.",
                            $"Home health: {Fake.Company.CompanyName()} Home Health will contact within 48 hours for wound care visits.",
                        ];
                        foreach (var instr in instructions)
                            col.Item().Text($"  - {instr}");
                        col.Item().PaddingTop(5);
                    }

                    col.Item().Text("FOLLOW-UP INSTRUCTIONS").Bold().FontSize(11);
                    var followUpDate = dischargeDate.AddDays(Rng.Next(7, 31));
                    col.Item().Text(FollowupParagraph(doctor, followUpDate.ToString("MM/dd/yyyy")));
                    col.Item().PaddingTop(8);

                    col.Item().Text($"Electronically signed by Dr. {doctor}");
                    col.Item().Text($"Date: {dischargeDate:MM/dd/yyyy hh:mm tt}");
                });
            });
        }).GeneratePdf(filepath);
    }

    static void MakeLabReport(string filepath, bool multipage)
    {
        var patient = GeneratePatientInfo();
        var doctor = Fake.Name.FullName();
        var facilityName = Fake.Company.CompanyName() + " Laboratory Services";
        var facilityAddr = Fake.Address.FullAddress();

        var numPanels = multipage ? Rng.Next(3, 6) : 1;
        var baseDate = Fake.Date.Past(0, DateTime.Now.AddDays(-3));
        var collectionDates = Enumerable.Range(0, numPanels).Select(d => baseDate.AddDays(d)).ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginHorizontal(40);
                page.MarginVertical(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text(facilityName).Bold().FontSize(14);
                    col.Item().AlignCenter().Text(facilityAddr).FontSize(9);
                    col.Item().PaddingVertical(4).LineHorizontal(1);
                    col.Item().PaddingTop(8);
                    col.Item().AlignCenter().Text("LABORATORY REPORT").Bold().FontSize(13);
                    col.Item().PaddingTop(5);

                    col.Item().Row(r => { r.RelativeItem().Text($"Patient: {patient["name"]}"); r.RelativeItem().Text($"MRN: {patient["mrn"]}"); });
                    col.Item().Row(r => { r.RelativeItem().Text($"DOB: {patient["dob"]}"); r.RelativeItem().Text($"Collection Date: {collectionDates[0]:MM/dd/yyyy}"); });
                    col.Item().Row(r => { r.RelativeItem().Text($"Ordering Physician: Dr. {doctor}"); r.RelativeItem().Text($"Report Date: {collectionDates[^1].AddDays(1):MM/dd/yyyy}"); });
                    col.Item().PaddingTop(8);

                    if (multipage)
                    {
                        for (int pi = 0; pi < Math.Min(numPanels, LabPanels.Length); pi++)
                        {
                            var (panelName, panelLabs) = LabPanels[pi];
                            var cd = collectionDates[Math.Min(pi, collectionDates.Count - 1)];
                            DrawLabTable(col, $"{panelName} - Collected {cd:MM/dd/yyyy}", panelLabs);
                        }

                        col.Item().Text("CLINICAL INTERPRETATION").Bold().FontSize(11);
                        for (int i = 0; i < Rng.Next(2, 5); i++)
                        {
                            col.Item().Text(ClinicalInterpretationParagraph());
                            col.Item().PaddingTop(3);
                        }
                    }
                    else
                    {
                        var labs = Fake.PickRandom(LabTests, Rng.Next(6, 13)).ToArray();
                        DrawLabTable(col, $"GENERAL PANEL - Collected {collectionDates[0]:MM/dd/yyyy}", labs);
                    }

                    col.Item().PaddingTop(3);
                    col.Item().Text("H = High, L = Low. Results outside reference range are flagged.").Italic().FontSize(9);
                    col.Item().PaddingTop(5);
                    col.Item().Text($"Verified by: {Fake.Name.FullName()}, MD, Lab Director");
                });
            });
        }).GeneratePdf(filepath);
    }

    static void DrawLabTable(ColumnDescriptor col, string title, (string Name, Func<string> ValueFn, string RefRange)[] labs)
    {
        col.Item().Text(title).Bold().FontSize(11);
        col.Item().PaddingTop(2);
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(2);
                c.RelativeColumn(2.5f);
                c.RelativeColumn(1.5f);
            });

            table.Header(h =>
            {
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text("Test").Bold();
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text("Result").Bold();
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text("Reference Range").Bold();
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text("Flag").Bold();
            });

            foreach (var (name, valueFn, refRange) in labs)
            {
                var flag = Fake.PickRandom("", "", "", "H", "L", "H", "");
                table.Cell().Border(0.5f).Padding(3).Text(name);
                table.Cell().Border(0.5f).Padding(3).Text(valueFn());
                table.Cell().Border(0.5f).Padding(3).Text(refRange);
                table.Cell().Border(0.5f).Padding(3).Text(flag);
            }
        });
        col.Item().PaddingTop(5);
    }

    static void MakePrescription(string filepath, bool multipage)
    {
        var patient = GeneratePatientInfo();
        var doctor = Fake.Name.FullName();
        var med = Fake.PickRandom(Medications);
        var rxDate = Fake.Date.Recent(14);
        var specialty = Fake.PickRandom("Internal Medicine", "Family Medicine", "Cardiology", "Endocrinology", "Pulmonology");
        var qty = Fake.PickRandom(30, 60, 90);
        var refills = Rng.Next(0, 6);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginHorizontal(40);
                page.MarginVertical(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text($"Dr. {doctor}").Bold().FontSize(14);
                    col.Item().AlignCenter().Text(specialty);
                    col.Item().AlignCenter().Text(Fake.Address.FullAddress()).FontSize(9);
                    col.Item().AlignCenter().Text($"Phone: {Fake.Phone.PhoneNumber()}  |  DEA#: {Fake.Random.Replace("??#######").ToUpper()}").FontSize(9);
                    col.Item().AlignCenter().Text($"NPI: {Fake.Random.Number(1000000000, int.MaxValue)}").FontSize(9);
                    col.Item().PaddingVertical(6).LineHorizontal(1);
                    col.Item().PaddingTop(12);

                    col.Item().Text($"Date: {rxDate:MM/dd/yyyy}").FontSize(11);
                    col.Item().Text($"Patient Name: {patient["name"]}").FontSize(11);
                    col.Item().Text($"DOB: {patient["dob"]}    MRN: {patient["mrn"]}").FontSize(11);
                    col.Item().Text($"Address: {patient["address"]}").FontSize(11);
                    col.Item().PaddingTop(10);

                    col.Item().Text("Rx").Bold().FontSize(24);
                    col.Item().PaddingTop(5);

                    col.Item().Text($"{med.Name} {med.Dose}").FontSize(13);
                    col.Item().Text($"Sig: Take {med.Freq}").FontSize(13);
                    col.Item().Text($"Qty: #{qty}    Refills: {refills}").FontSize(13);
                    col.Item().Text($"DAW: {Fake.PickRandom("Yes", "No")}").FontSize(13);
                    col.Item().PaddingTop(15);

                    col.Item().LineHorizontal(0.5f);
                    col.Item().PaddingTop(3);
                    col.Item().Text($"Prescriber Signature: Dr. {doctor}");
                    col.Item().Text($"Date: {rxDate:MM/dd/yyyy}");
                });
            });
        }).GeneratePdf(filepath);
    }
}
