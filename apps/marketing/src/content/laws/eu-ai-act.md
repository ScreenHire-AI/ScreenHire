---
title: 'EU AI Act and hiring: dates and duties'
description: 'How the EU AI Act treats AI in recruitment: the emotion-recognition ban in force since 2025 and high-risk duties from 2 December 2027.'
name: EU AI Act
jurisdiction: European Union
status: 'Bans in force since 2 February 2025. High-risk duties for recruitment apply from 2 December 2027.'
published: '2026-10-18'
lastReviewed: '2026-10-18'
order: 1
keyDates:
  - date: '2 February 2025'
    event: 'Article 5 bans apply, including emotion recognition in the workplace. The 2026 amendments did not relax them.'
  - date: '24 July 2026'
    event: 'The "Digital Omnibus" amending regulation, Regulation (EU) 2026/1744, is published in the Official Journal.'
  - date: '2 December 2027'
    event: 'Obligations for high-risk systems listed in Annex III, which includes recruitment, apply.'
  - date: '2 August 2028'
    event: 'Obligations for high-risk systems under Annex I apply.'
sources:
  - label: 'Regulation (EU) 2026/1744 of 8 July 2026, EUR-Lex'
    url: 'https://eur-lex.europa.eu/eli/reg/2026/1744/oj/eng'
  - label: 'Gibson Dunn, "EU AI Act omnibus agreement: postponed high-risk deadlines and other key changes", 27 May 2026'
    url: 'https://www.gibsondunn.com/eu-ai-act-omnibus-agreement-postponed-high-risk-deadlines-and-other-key-changes/'
  - label: 'Future of Privacy Forum, "Red lines under the EU AI Act: emotion recognition in the workplace", 24 March 2026'
    url: 'https://fpf.org/blog/red-lines-under-eu-ai-act-unpacking-the-prohibition-of-emotion-recognition-in-the-workplace-and-education-institutions/'
  - label: 'European Commission, guidelines on prohibited AI practices'
    url: 'https://digital-strategy.ec.europa.eu/en/library/commission-publishes-guidelines-prohibited-artificial-intelligence-ai-practices-defined-ai-act'
faqs:
  - question: 'Is AI used in recruitment high-risk under the EU AI Act?'
    answer: 'AI systems used for recruitment and selection are listed in Annex III, point 4(a). A system that profiles people cannot use the Article 6(3) exemption, so it is high-risk. Those obligations apply from 2 December 2027.'
  - question: 'Can hiring software detect emotions or stress in candidates?'
    answer: 'Not to infer emotions. Article 5(1)(f) bans emotion recognition in the workplace, and the Commission guidelines include recruitment. The ban has applied since 2 February 2025.'
---

## What it is

The EU AI Act regulates AI systems by the risk they pose. Some practices are banned outright. Systems in sensitive areas, recruitment among them, are "high-risk" and carry duties for the companies that build them (providers) and the companies that use them (deployers).

In July 2026 the EU adopted amendments, known as the Digital Omnibus, that moved the start date for high-risk duties under Annex III to **2 December 2027** [1][2]. The bans in Article 5 were not relaxed and have applied since 2 February 2025. The AI-literacy duty in Article 4 was softened to "support" [1][2].

## What it means if you use software to screen applicants

- **Recruitment is high-risk.** Annex III, point 4(a) covers AI used to recruit or select people. A system that profiles applicants cannot rely on the Article 6(3) exemption.
- **Emotion recognition is banned in hiring.** Article 5(1)(f) prohibits inferring emotions in the workplace, and the Commission's guidelines include recruitment [3][4]. The AI Act's definition of biometric data has no identification test, so the ban can reach typing patterns used to infer emotion.
- **Physical states are outside the ban.** The Commission guidelines treat physical states, such as fatigue, as outside "emotion" [3].

## How ScreenHire is designed around it

- No emotion, stress, honesty, intent or personality inference from text, typing, face or voice, anywhere in the product.
- Integrity output is a level with reason codes, evidence and counter-evidence. There is no opaque score, and integrity signals never reject anyone.
- Levels come from versioned, documented rules, and every assessment records which ruleset produced it, so it can be explained and audited.
- We are building to the provider standard for high-risk systems now, ahead of the 2027 date.

## Still unclear

- Whether session-level bot and automation detection can fall outside Annex III. The Commission's guidance on Article 6 has not settled it.
- Whether "intentions" fall under the Article 5(1)(f) ban. The Dutch data protection authority reads it broadly, and detecting off-screen gaze is a borderline case [3].
