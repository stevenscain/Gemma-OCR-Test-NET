using Bogus;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;
MedicalPdfGenerator.Run(args);

static class MedicalPdfGenerator
{
    static readonly Faker Fake = new("en");
    static readonly Random Rng = new();

    // ── Ground truth text capture ─────────────────────────────────────────
    static List<string>? _gt;

    static string GT(string s) { _gt?.Add(s); return s; }

    static void SaveGT(string pdfPath)
    {
        if (_gt is not { Count: > 0 }) return;
        File.WriteAllLines(Path.ChangeExtension(pdfPath, ".txt"), _gt);
        _gt.Clear();
    }

    // ── East Coast US locations ───────────────────────────────────────────

    static readonly (string City, string State, string Abbr, string[] Zips, string[] AreaCodes)[] EastCoastCities =
    [
        ("New York", "New York", "NY", ["10001", "10016", "10029", "10032", "10065"], ["212", "718", "917"]),
        ("Boston", "Massachusetts", "MA", ["02114", "02115", "02118", "02120", "02215"], ["617", "857"]),
        ("Philadelphia", "Pennsylvania", "PA", ["19104", "19107", "19111", "19120", "19140"], ["215", "267"]),
        ("Baltimore", "Maryland", "MD", ["21201", "21205", "21218", "21224", "21231"], ["410", "443"]),
        ("Washington", "District of Columbia", "DC", ["20001", "20007", "20010", "20037", "20052"], ["202"]),
        ("Charlotte", "North Carolina", "NC", ["28202", "28204", "28207", "28210", "28216"], ["704", "980"]),
        ("Raleigh", "North Carolina", "NC", ["27601", "27603", "27607", "27609", "27612"], ["919", "984"]),
        ("Richmond", "Virginia", "VA", ["23219", "23220", "23225", "23230", "23298"], ["804"]),
        ("Atlanta", "Georgia", "GA", ["30303", "30308", "30312", "30322", "30342"], ["404", "678", "770"]),
        ("Miami", "Florida", "FL", ["33101", "33125", "33136", "33140", "33155"], ["305", "786"]),
        ("Jacksonville", "Florida", "FL", ["32099", "32204", "32207", "32209", "32216"], ["904"]),
        ("Pittsburgh", "Pennsylvania", "PA", ["15213", "15219", "15224", "15232", "15260"], ["412"]),
        ("Newark", "New Jersey", "NJ", ["07102", "07103", "07104", "07107", "07112"], ["973", "862"]),
        ("Hartford", "Connecticut", "CT", ["06103", "06105", "06106", "06112", "06120"], ["860", "959"]),
        ("Charleston", "South Carolina", "SC", ["29401", "29403", "29407", "29412", "29425"], ["843"]),
        ("Savannah", "Georgia", "GA", ["31401", "31404", "31405", "31406", "31419"], ["912"]),
        ("Portland", "Maine", "ME", ["04101", "04102", "04103", "04106"], ["207"]),
        ("Providence", "Rhode Island", "RI", ["02903", "02904", "02906", "02908"], ["401"]),
    ];

    static readonly string[] EastCoastStreets =
    [
        "Main St", "Elm St", "Oak Ave", "Maple Dr", "Cedar Ln", "Pine St",
        "Washington Blvd", "Park Ave", "Church St", "Broad St", "Market St",
        "Peachtree Rd", "Atlantic Ave", "Commonwealth Ave", "Beacon St",
        "Chestnut St", "Walnut St", "Spruce St", "King St", "Meeting St",
        "Memorial Dr", "University Pkwy", "Hospital Dr", "Medical Center Blvd",
    ];

    static string EastCoastAddress()
    {
        var city = Fake.PickRandom(EastCoastCities);
        var number = Rng.Next(100, 9999);
        var street = Fake.PickRandom(EastCoastStreets);
        var zip = Fake.PickRandom(city.Zips);
        return $"{number} {street}, {city.City}, {city.Abbr} {zip}";
    }

    static string EastCoastPhone()
    {
        var city = Fake.PickRandom(EastCoastCities);
        var ac = Fake.PickRandom(city.AreaCodes);
        return $"({ac}) {Rng.Next(200, 999):D3}-{Rng.Next(1000, 9999):D4}";
    }

    public static void Run(string[] args)
    {
        if (args.Contains("--ground-truth"))
            _gt = [];

        var outputDir = Path.Combine("..", "test-pdfs");
        Directory.CreateDirectory(outputDir);
        var total = 0;

        void Generate(string docType, int count, bool multipage, Action<string, bool> gen)
        {
            for (int i = 1; i <= count; i++)
            {
                var filename = $"{docType}_{i:D2}.pdf";
                var filepath = Path.Combine(outputDir, filename);
                _gt?.Clear();
                gen(filepath, multipage);
                SaveGT(filepath);
                Console.WriteLine($"Generated: {filepath}");
                total++;
            }
        }

        Generate("discharge_summary", 3, false, MakeDischargeSummary);
        Generate("lab_report", 3, false, MakeLabReport);
        Generate("prescription", 4, false, MakePrescription);
        Generate("discharge_summary_multi", 2, true, MakeDischargeSummary);
        Generate("lab_report_multi", 2, true, MakeLabReport);

        // Commingled stress tests — sensitive content placed at page boundaries
        foreach (var interval in new[] { 1, 2, 3 })
        {
            var filename = $"commingled_boundary_{interval}pg.pdf";
            var filepath = Path.Combine(outputDir, filename);
            _gt?.Clear();
            MakeCommingledStressTest(filepath, pagesPerPatient: interval, patientCount: 6);
            SaveGT(filepath);
            Console.WriteLine($"Generated: {filepath}");
            total++;
        }

        Console.WriteLine($"\nDone! Generated {total} synthetic medical PDFs in '{outputDir}/'");
        if (_gt != null) Console.WriteLine("Ground truth .txt files written alongside each PDF.");
    }

    // ── Patient / Vitals ─────────────────────────────────────────────────────

    static Dictionary<string, string> GeneratePatientInfo() => new()
    {
        ["name"] = Fake.Name.FullName(),
        ["dob"] = Fake.Date.Past(72, DateTime.Now.AddYears(-18)).ToString("MM/dd/yyyy"),
        ["ssn"] = Fake.Random.Replace("###-##-####"),
        ["address"] = EastCoastAddress(),
        ["phone"] = EastCoastPhone(),
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
        var facilityAddr = EastCoastAddress();
        var facilityPhone = EastCoastPhone();
        var facilityFax = EastCoastPhone();

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
                    col.Item().AlignCenter().Text(GT(facilityName)).Bold().FontSize(16);
                    col.Item().AlignCenter().Text(GT(facilityAddr)).FontSize(9);
                    col.Item().AlignCenter().Text(GT($"Phone: {facilityPhone}  |  Fax: {facilityFax}")).FontSize(9);
                    col.Item().PaddingVertical(4).LineHorizontal(1);
                    col.Item().PaddingTop(5);
                    col.Item().AlignCenter().Text(GT("DISCHARGE SUMMARY")).Bold().FontSize(14);
                    col.Item().PaddingTop(5);

                    col.Item().Text(GT("PATIENT INFORMATION")).Bold().FontSize(11);
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"Patient Name: {patient["name"]}")); r.RelativeItem().Text(GT($"MRN: {patient["mrn"]}")); });
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"Date of Birth: {patient["dob"]}")); r.RelativeItem().Text(GT($"Insurance ID: {patient["insurance_id"]}")); });
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"Admission Date: {admitDate:MM/dd/yyyy}")); r.RelativeItem().Text(GT($"Discharge Date: {dischargeDate:MM/dd/yyyy}")); });
                    col.Item().Text(GT($"Attending Physician: Dr. {doctor}"));
                    col.Item().PaddingTop(5);

                    col.Item().Text(GT("VITALS AT DISCHARGE")).Bold().FontSize(11);
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"BP: {vitals["blood_pressure"]}")); r.RelativeItem().Text(GT($"HR: {vitals["heart_rate"]}")); });
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"Temp: {vitals["temperature"]}")); r.RelativeItem().Text(GT($"SpO2: {vitals["oxygen_saturation"]}")); });
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"Resp Rate: {vitals["respiratory_rate"]}")); r.RelativeItem().Text(GT($"Weight: {vitals["weight"]}")); });
                    col.Item().PaddingTop(5);

                    col.Item().Text(GT("DIAGNOSES")).Bold().FontSize(11);
                    for (int i = 0; i < diagnoses.Count; i++)
                        col.Item().Text(GT($"  {i + 1}. {diagnoses[i]}"));
                    col.Item().PaddingTop(5);

                    col.Item().Text(GT("HOSPITAL COURSE")).Bold().FontSize(11);
                    var numParagraphs = multipage ? Rng.Next(4, 8) : 1;
                    for (int i = 0; i < numParagraphs; i++)
                    {
                        col.Item().Text(GT(HospitalCourseParagraph()));
                        col.Item().PaddingTop(3);
                    }
                    col.Item().PaddingTop(2);

                    col.Item().Text(GT("DISCHARGE MEDICATIONS")).Bold().FontSize(11);
                    for (int i = 0; i < meds.Count; i++)
                        col.Item().Text(GT($"  {i + 1}. {meds[i].Name} {meds[i].Dose} - {meds[i].Freq}"));
                    col.Item().PaddingTop(5);

                    if (multipage)
                    {
                        col.Item().Text(GT("PROCEDURES PERFORMED")).Bold().FontSize(11);
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
                            col.Item().Text(GT($"  {i + 1}. {selectedProcs[i]}"));
                        col.Item().PaddingTop(5);

                        col.Item().Text(GT("CONSULTATION NOTES")).Bold().FontSize(11);
                        string[] specialties = ["Cardiology", "Pulmonology", "Infectious Disease", "Nephrology", "Endocrinology"];
                        var selectedSpecs = Fake.PickRandom(specialties, Rng.Next(2, 5)).ToList();
                        foreach (var spec in selectedSpecs)
                        {
                            col.Item().Text(GT($"  {spec} - Dr. {Fake.Name.FullName()}")).Bold();
                            col.Item().Text(GT($"    {ConsultationNote(spec)}"));
                            col.Item().PaddingTop(3);
                        }
                        col.Item().PaddingTop(5);

                        col.Item().Text(GT("PATIENT EDUCATION & DISCHARGE INSTRUCTIONS")).Bold().FontSize(11);
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
                            col.Item().Text(GT($"  - {instr}"));
                        col.Item().PaddingTop(5);
                    }

                    col.Item().Text(GT("FOLLOW-UP INSTRUCTIONS")).Bold().FontSize(11);
                    var followUpDate = dischargeDate.AddDays(Rng.Next(7, 31));
                    col.Item().Text(GT(FollowupParagraph(doctor, followUpDate.ToString("MM/dd/yyyy"))));
                    col.Item().PaddingTop(8);

                    col.Item().Text(GT($"Electronically signed by Dr. {doctor}"));
                    col.Item().Text(GT($"Date: {dischargeDate:MM/dd/yyyy hh:mm tt}"));
                });
            });
        }).GeneratePdf(filepath);
    }

    static void MakeLabReport(string filepath, bool multipage)
    {
        var patient = GeneratePatientInfo();
        var doctor = Fake.Name.FullName();
        var facilityName = Fake.Company.CompanyName() + " Laboratory Services";
        var facilityAddr = EastCoastAddress();

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
                    col.Item().AlignCenter().Text(GT(facilityName)).Bold().FontSize(14);
                    col.Item().AlignCenter().Text(GT(facilityAddr)).FontSize(9);
                    col.Item().PaddingVertical(4).LineHorizontal(1);
                    col.Item().PaddingTop(8);
                    col.Item().AlignCenter().Text(GT("LABORATORY REPORT")).Bold().FontSize(13);
                    col.Item().PaddingTop(5);

                    col.Item().Row(r => { r.RelativeItem().Text(GT($"Patient: {patient["name"]}")); r.RelativeItem().Text(GT($"MRN: {patient["mrn"]}")); });
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"DOB: {patient["dob"]}")); r.RelativeItem().Text(GT($"Collection Date: {collectionDates[0]:MM/dd/yyyy}")); });
                    col.Item().Row(r => { r.RelativeItem().Text(GT($"Ordering Physician: Dr. {doctor}")); r.RelativeItem().Text(GT($"Report Date: {collectionDates[^1].AddDays(1):MM/dd/yyyy}")); });
                    col.Item().PaddingTop(8);

                    if (multipage)
                    {
                        for (int pi = 0; pi < Math.Min(numPanels, LabPanels.Length); pi++)
                        {
                            var (panelName, panelLabs) = LabPanels[pi];
                            var cd = collectionDates[Math.Min(pi, collectionDates.Count - 1)];
                            DrawLabTable(col, $"{panelName} - Collected {cd:MM/dd/yyyy}", panelLabs);
                        }

                        col.Item().Text(GT("CLINICAL INTERPRETATION")).Bold().FontSize(11);
                        for (int i = 0; i < Rng.Next(2, 5); i++)
                        {
                            col.Item().Text(GT(ClinicalInterpretationParagraph()));
                            col.Item().PaddingTop(3);
                        }
                    }
                    else
                    {
                        var labs = Fake.PickRandom(LabTests, Rng.Next(6, 13)).ToArray();
                        DrawLabTable(col, $"GENERAL PANEL - Collected {collectionDates[0]:MM/dd/yyyy}", labs);
                    }

                    col.Item().PaddingTop(3);
                    col.Item().Text(GT("H = High, L = Low. Results outside reference range are flagged.")).Italic().FontSize(9);
                    col.Item().PaddingTop(5);
                    col.Item().Text(GT($"Verified by: {Fake.Name.FullName()}, MD, Lab Director"));
                });
            });
        }).GeneratePdf(filepath);
    }

    static void DrawLabTable(ColumnDescriptor col, string title, (string Name, Func<string> ValueFn, string RefRange)[] labs)
    {
        col.Item().Text(GT(title)).Bold().FontSize(11);
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
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text(GT("Test")).Bold();
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text(GT("Result")).Bold();
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text(GT("Reference Range")).Bold();
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).Text(GT("Flag")).Bold();
            });

            foreach (var (name, valueFn, refRange) in labs)
            {
                var val = valueFn();
                var flag = Fake.PickRandom("", "", "", "H", "L", "H", "");
                table.Cell().Border(0.5f).Padding(3).Text(GT(name));
                table.Cell().Border(0.5f).Padding(3).Text(GT(val));
                table.Cell().Border(0.5f).Padding(3).Text(GT(refRange));
                table.Cell().Border(0.5f).Padding(3).Text(GT(flag));
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
                    col.Item().AlignCenter().Text(GT($"Dr. {doctor}")).Bold().FontSize(14);
                    col.Item().AlignCenter().Text(GT(specialty));
                    col.Item().AlignCenter().Text(GT(EastCoastAddress())).FontSize(9);
                    col.Item().AlignCenter().Text(GT($"Phone: {EastCoastPhone()}  |  DEA#: {Fake.Random.Replace("??#######").ToUpper()}")).FontSize(9);
                    col.Item().AlignCenter().Text(GT($"NPI: {Fake.Random.Number(1000000000, int.MaxValue)}")).FontSize(9);
                    col.Item().PaddingVertical(6).LineHorizontal(1);
                    col.Item().PaddingTop(12);

                    col.Item().Text(GT($"Date: {rxDate:MM/dd/yyyy}")).FontSize(11);
                    col.Item().Text(GT($"Patient Name: {patient["name"]}")).FontSize(11);
                    col.Item().Text(GT($"DOB: {patient["dob"]}    MRN: {patient["mrn"]}")).FontSize(11);
                    col.Item().Text(GT($"Address: {patient["address"]}")).FontSize(11);
                    col.Item().PaddingTop(10);

                    col.Item().Text(GT("Rx")).Bold().FontSize(24);
                    col.Item().PaddingTop(5);

                    col.Item().Text(GT($"{med.Name} {med.Dose}")).FontSize(13);
                    col.Item().Text(GT($"Sig: Take {med.Freq}")).FontSize(13);
                    col.Item().Text(GT($"Qty: #{qty}    Refills: {refills}")).FontSize(13);
                    col.Item().Text(GT($"DAW: {Fake.PickRandom("Yes", "No")}")).FontSize(13);
                    col.Item().PaddingTop(15);

                    col.Item().LineHorizontal(0.5f);
                    col.Item().PaddingTop(3);
                    col.Item().Text(GT($"Prescriber Signature: Dr. {doctor}"));
                    col.Item().Text(GT($"Date: {rxDate:MM/dd/yyyy}"));
                });
            });
        }).GeneratePdf(filepath);
    }

    // ── Sensitive content for stress tests ────────────────────────────────────

    static readonly string[] SubstanceAbuseNotes =
    [
        "Patient has a documented history of alcohol use disorder, moderate severity, currently in early remission per DSM-5 criteria (F10.20).",
        "Urine drug screen on admission was positive for benzodiazepines and opioids; patient reports prescribed use only.",
        "Patient admitted to daily cannabis use for the past 3 years; counseled on cessation and referred to addiction services.",
        "History of intravenous heroin use with last reported use 6 months prior to admission; Hepatitis C antibody positive.",
        "Methadone maintenance therapy continued at 80mg daily during hospitalization per outpatient opioid treatment program.",
        "CAGE questionnaire score of 3/4; patient acknowledges problematic alcohol consumption averaging 12-15 drinks per week.",
        "Naloxone rescue kit prescribed at discharge given history of opioid use disorder; patient and family educated on administration.",
        "Patient previously completed 28-day residential treatment program for cocaine dependence; reports 14 months of sobriety.",
        "AUDIT-C score of 8 indicates hazardous drinking pattern; brief intervention performed with motivational interviewing techniques.",
        "Buprenorphine/naloxone (Suboxone) 8mg/2mg sublingual film initiated for opioid use disorder during this admission.",
    ];

    static readonly string[] MentalHealthNotes =
    [
        "Patient carries an active diagnosis of Bipolar I Disorder with psychotic features (F31.2); lithium levels monitored during admission.",
        "PHQ-9 score of 22 on admission consistent with severe major depressive episode; psychiatric consultation requested.",
        "Patient reports active suicidal ideation with plan but no intent; 1:1 safety observation initiated per hospital protocol.",
        "History of three prior psychiatric hospitalizations for acute mania with grandiose delusions and decreased need for sleep.",
        "Patient diagnosed with Posttraumatic Stress Disorder (F43.10) secondary to military combat exposure; nightmares and hypervigilance reported.",
        "Schizoaffective disorder, depressive type, managed with paliperidone palmitate (Invega Sustenna) 156mg IM monthly.",
        "Patient endorsed auditory hallucinations described as commanding voices; antipsychotic medication adjusted accordingly.",
        "Psychiatric advance directive on file designating spouse as healthcare agent for mental health treatment decisions.",
        "Eating disorder history documented: anorexia nervosa, restricting type, with BMI of 16.2 on admission; nutrition consult placed.",
        "Patient has documented history of self-harm behavior including cutting; last episode reported 3 weeks prior to admission.",
    ];

    static readonly string[] HivStiNotes =
    [
        "Patient is HIV-positive, diagnosed in 2019, currently on antiretroviral therapy: Biktarvy (bictegravir/emtricitabine/tenofovir alafenamide) daily.",
        "CD4 count obtained during admission: 485 cells/mm3; viral load undetectable (<20 copies/mL) confirming virologic suppression.",
        "HIV genotype resistance testing performed; no major resistance mutations identified on current regimen.",
        "RPR reactive with titer of 1:64; confirmatory FTA-ABS positive consistent with secondary syphilis; IM penicillin G administered.",
        "Gonorrhea and chlamydia NAAT screening performed on pharyngeal, rectal, and urogenital specimens; results pending at discharge.",
        "Patient reports inconsistent condom use with multiple sexual partners; PrEP counseling provided and referral to sexual health clinic.",
        "Hepatitis B surface antigen positive; HBV DNA viral load of 45,000 IU/mL; hepatology referral for treatment initiation.",
        "Patient diagnosed with genital herpes simplex virus type 2; valacyclovir 1g BID initiated for acute outbreak management.",
    ];

    static readonly string[] GeneticTestingNotes =
    [
        "BRCA1 pathogenic variant identified (c.5266dupC); patient referred to genetic counselor and surgical oncology for risk-reducing options.",
        "Pharmacogenomic testing revealed CYP2D6 poor metabolizer status; codeine and tramadol contraindicated per FDA labeling.",
        "Huntington disease predictive testing: CAG repeat expansion of 42 repeats identified; presymptomatic genetic counseling provided.",
        "Lynch syndrome confirmed: MSH2 pathogenic variant detected; enhanced colorectal and endometrial cancer surveillance recommended.",
        "Factor V Leiden heterozygous mutation identified; lifelong anticoagulation therapy discussed given recurrent VTE history.",
        "Whole exome sequencing identified pathogenic variant in CFTR gene consistent with cystic fibrosis carrier status.",
        "Hereditary hemochromatosis: HFE C282Y homozygous genotype confirmed; therapeutic phlebotomy schedule initiated.",
    ];

    static readonly string[] DomesticViolenceNotes =
    [
        "Intimate partner violence screening positive; patient reports physical abuse by current partner occurring 2-3 times monthly.",
        "Safety assessment completed: patient states she does not feel safe returning home; social work consulted for shelter placement.",
        "Photographs of injuries documented per forensic protocol with patient consent; injuries inconsistent with reported mechanism.",
        "Pattern of injuries concerning for non-accidental trauma: multiple bruises in varying stages of healing on bilateral upper extremities.",
        "Patient provided with National Domestic Violence Hotline number (1-800-799-7233) and local advocacy resources.",
        "Mandatory reporting completed per state law given suspected abuse involving a vulnerable adult; Adult Protective Services notified.",
    ];

    static readonly string[] FillerNarrative =
    [
        "The patient's vital signs remained stable throughout the shift with no acute changes noted on continuous monitoring.",
        "Nursing assessment performed at the bedside; patient is resting comfortably with no complaints of pain or distress.",
        "Intravenous fluids continued at maintenance rate; intake and output documented and within acceptable parameters.",
        "Fall risk assessment completed using the Morse Fall Scale; patient scored moderate risk and appropriate precautions implemented.",
        "Patient education provided regarding the importance of ambulation and incentive spirometry use to prevent postoperative complications.",
        "Interdisciplinary team rounds completed with input from medicine, nursing, pharmacy, physical therapy, and case management.",
        "Laboratory specimens collected per physician orders and sent to the lab; results anticipated within standard turnaround time.",
        "Patient's family visited and was updated on the plan of care by the attending physician during afternoon rounds.",
        "Respiratory therapy performed scheduled nebulizer treatment; patient tolerated well with improved breath sounds bilaterally.",
        "Medication reconciliation reviewed with patient and pharmacy; no discrepancies identified between home and inpatient medication lists.",
        "Wound care performed per protocol with sterile technique; wound bed appears clean and granulating without signs of infection.",
        "Physical therapy facilitated ambulation in the hallway with rolling walker; patient ambulated 150 feet with minimal assistance.",
        "Occupational therapy assessed patient's ability to perform activities of daily living; mild difficulty with fine motor tasks noted.",
        "Dietary tray delivered; patient consumed approximately 75% of meal with good appetite noted by nursing staff.",
        "Pain management reassessed per scheduled protocol; patient reports pain level of 3/10 with current medication regimen.",
        "Blood glucose monitoring performed per sliding scale protocol; values within target range and insulin administered as ordered.",
        "Patient participated in bedside physical therapy exercises including ankle pumps, quad sets, and straight leg raises.",
        "Chaplain services offered to patient and family; patient declined at this time but is aware services are available.",
        "Sequential compression devices applied bilaterally for DVT prophylaxis; patient instructed to keep devices on while in bed.",
        "Foley catheter care performed; output adequate and within expected range; plan to discontinue catheter per protocol tomorrow.",
    ];

    // ── Commingled stress test generator ─────────────────────────────────────

    static void MakeCommingledStressTest(string filepath, int pagesPerPatient, int patientCount)
    {
        // Each patient record is designed to fill ~pagesPerPatient pages.
        // Sensitive content is placed at the END of each patient's block so it
        // naturally falls across a page boundary and bleeds into the next patient.
        // This means if you're chunking at pagesPerPatient pages, the chunk
        // boundary will land right in the middle of sensitive data or at the
        // transition between two patients.

        // Approximate lines per page at 10pt with spacing: ~45 content items
        var itemsPerPage = 45;
        var targetItems = pagesPerPatient * itemsPerPage;

        // Sensitive content takes about 20-25 items
        var sensitiveItems = 22;
        var fillerNeeded = targetItems - sensitiveItems - 15; // 15 for header/demographics

        var facilityName = Fake.Company.CompanyName() + " Regional Medical Center";
        var facilityAddr = EastCoastAddress();

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
                    // Document header
                    col.Item().AlignCenter().Text(GT(facilityName)).Bold().FontSize(14);
                    col.Item().AlignCenter().Text(GT(facilityAddr)).FontSize(9);
                    col.Item().AlignCenter().Text(GT("CONSOLIDATED PATIENT RECORDS — CONFIDENTIAL")).Bold().FontSize(11);
                    col.Item().PaddingVertical(4).LineHorizontal(1);
                    col.Item().PaddingTop(3);
                    col.Item().Text(GT($"Report Generated: {DateTime.Now:MM/dd/yyyy hh:mm tt}")).FontSize(9);
                    col.Item().Text(GT($"Boundary Interval: {pagesPerPatient} page(s) per patient block")).FontSize(9).Italic();
                    col.Item().PaddingTop(5);

                    for (int p = 0; p < patientCount; p++)
                    {
                        var patient = GeneratePatientInfo();
                        var vitals = GenerateVitals();
                        var doctor = Fake.Name.FullName();
                        var admitDate = Fake.Date.Past(0, DateTime.Now.AddDays(-2));
                        var dischargeDate = admitDate.AddDays(Rng.Next(2, 12));
                        var diagnoses = Fake.PickRandom(Diagnoses, Rng.Next(3, 6)).ToList();
                        var meds = Fake.PickRandom(Medications, Rng.Next(4, 8)).ToList();

                        // ── Patient header & demographics ──
                        col.Item().PaddingTop(3).LineHorizontal(2);
                        col.Item().PaddingTop(3);
                        col.Item().Text(GT($"PATIENT {p + 1} OF {patientCount}")).Bold().FontSize(12);
                        col.Item().PaddingTop(3);
                        col.Item().Row(r => { r.RelativeItem().Text(GT($"Name: {patient["name"]}")).Bold(); r.RelativeItem().Text(GT($"MRN: {patient["mrn"]}")); });
                        col.Item().Row(r => { r.RelativeItem().Text(GT($"DOB: {patient["dob"]}")); r.RelativeItem().Text(GT($"SSN: {patient["ssn"]}")); });
                        col.Item().Row(r => { r.RelativeItem().Text(GT($"Address: {patient["address"]}")); r.RelativeItem().Text(GT($"Phone: {patient["phone"]}")); });
                        col.Item().Row(r => { r.RelativeItem().Text(GT($"Insurance: {patient["insurance_id"]}")); r.RelativeItem().Text(GT($"Attending: Dr. {doctor}")); });
                        col.Item().Row(r => { r.RelativeItem().Text(GT($"Admitted: {admitDate:MM/dd/yyyy}")); r.RelativeItem().Text(GT($"Discharged: {dischargeDate:MM/dd/yyyy}")); });
                        col.Item().PaddingTop(3);

                        // Vitals
                        col.Item().Text(GT("VITALS")).Bold();
                        col.Item().Text(GT($"BP: {vitals["blood_pressure"]}  HR: {vitals["heart_rate"]}  Temp: {vitals["temperature"]}  SpO2: {vitals["oxygen_saturation"]}  RR: {vitals["respiratory_rate"]}"));
                        col.Item().PaddingTop(3);

                        // Diagnoses
                        col.Item().Text(GT("DIAGNOSES")).Bold();
                        foreach (var dx in diagnoses)
                            col.Item().Text(GT($"  • {dx}"));
                        col.Item().PaddingTop(3);

                        // Medications
                        col.Item().Text(GT("MEDICATIONS")).Bold();
                        foreach (var med in meds)
                            col.Item().Text(GT($"  • {med.Name} {med.Dose} — {med.Freq}"));
                        col.Item().PaddingTop(3);

                        // ── Filler: hospital course narrative to push toward page boundary ──
                        col.Item().Text(GT("HOSPITAL COURSE")).Bold();
                        var fillerLines = Math.Max(fillerNeeded, 5);
                        var fillerText = new List<string>();
                        while (fillerText.Count < fillerLines)
                            fillerText.AddRange(Fake.PickRandom(FillerNarrative, Math.Min(8, fillerLines - fillerText.Count)));
                        foreach (var line in fillerText.Take(fillerLines))
                            col.Item().Text(GT(line));
                        col.Item().PaddingTop(3);

                        // ── SENSITIVE CONTENT — deliberately at page boundary ──
                        // This section is designed to be split across the page break
                        // so that when chunking at pagesPerPatient, the chunk boundary
                        // falls in the middle of this sensitive data.

                        var sensitiveType = p % 5;
                        switch (sensitiveType)
                        {
                            case 0: // Substance abuse
                                col.Item().Text(GT("SUBSTANCE ABUSE & ADDICTION ASSESSMENT")).Bold();
                                col.Item().Text(GT($"Patient: {patient["name"]}  SSN: {patient["ssn"]}  MRN: {patient["mrn"]}"));
                                foreach (var note in Fake.PickRandom(SubstanceAbuseNotes, Rng.Next(4, 7)))
                                    col.Item().Text(GT(note));
                                col.Item().Text(GT($"Assessed by: Dr. {Fake.Name.FullName()}, Addiction Medicine"));
                                col.Item().Text(GT("42 CFR Part 2 protected information — unauthorized disclosure prohibited."));
                                break;

                            case 1: // Mental health
                                col.Item().Text(GT("PSYCHIATRIC EVALUATION & MENTAL HEALTH NOTES")).Bold();
                                col.Item().Text(GT($"Patient: {patient["name"]}  DOB: {patient["dob"]}  SSN: {patient["ssn"]}"));
                                foreach (var note in Fake.PickRandom(MentalHealthNotes, Rng.Next(4, 7)))
                                    col.Item().Text(GT(note));
                                col.Item().Text(GT($"Evaluating Psychiatrist: Dr. {Fake.Name.FullName()}, MD, Board Certified Psychiatry"));
                                col.Item().Text(GT("Protected mental health information per state and federal law."));
                                break;

                            case 2: // HIV/STI
                                col.Item().Text(GT("HIV/STI SCREENING & TREATMENT NOTES")).Bold();
                                col.Item().Text(GT($"Patient: {patient["name"]}  MRN: {patient["mrn"]}  Insurance: {patient["insurance_id"]}"));
                                foreach (var note in Fake.PickRandom(HivStiNotes, Rng.Next(4, 6)))
                                    col.Item().Text(GT(note));
                                col.Item().Text(GT($"Infectious Disease Consult: Dr. {Fake.Name.FullName()}"));
                                col.Item().Text(GT("HIV-related information protected under state confidentiality statutes."));
                                break;

                            case 3: // Genetic testing
                                col.Item().Text(GT("GENETIC TESTING RESULTS & COUNSELING NOTES")).Bold();
                                col.Item().Text(GT($"Patient: {patient["name"]}  DOB: {patient["dob"]}  SSN: {patient["ssn"]}"));
                                foreach (var note in Fake.PickRandom(GeneticTestingNotes, Rng.Next(3, 6)))
                                    col.Item().Text(GT(note));
                                col.Item().Text(GT($"Genetic Counselor: {Fake.Name.FullName()}, MS, CGC"));
                                col.Item().Text(GT("GINA-protected genetic information — disclosure restrictions apply."));
                                break;

                            case 4: // Domestic violence
                                col.Item().Text(GT("DOMESTIC VIOLENCE / INTIMATE PARTNER VIOLENCE SCREENING")).Bold();
                                col.Item().Text(GT($"Patient: {patient["name"]}  Address: {patient["address"]}  Phone: {patient["phone"]}"));
                                foreach (var note in Fake.PickRandom(DomesticViolenceNotes, Rng.Next(3, 5)))
                                    col.Item().Text(GT(note));
                                col.Item().Text(GT($"Screened by: {Fake.Name.FullName()}, LCSW"));
                                col.Item().Text(GT("Sensitive domestic violence documentation — restricted access per hospital policy."));
                                break;
                        }

                        col.Item().PaddingTop(2);
                        col.Item().Text(GT($"— End of record for {patient["name"]} (MRN: {patient["mrn"]}) —")).Italic().FontSize(9);
                        // NO page break — next patient starts immediately to create commingling
                        col.Item().PaddingTop(3);
                    }

                    col.Item().PaddingTop(10).LineHorizontal(2);
                    col.Item().PaddingTop(3);
                    col.Item().Text(GT("END OF CONSOLIDATED PATIENT RECORDS")).Bold().AlignCenter();
                    col.Item().Text(GT($"Total patients: {patientCount}  |  Generated: {DateTime.Now:MM/dd/yyyy}")).AlignCenter().FontSize(9);
                });
            });
        }).GeneratePdf(filepath);
    }
}
