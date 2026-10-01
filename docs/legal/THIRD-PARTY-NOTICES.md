# Third-party notices

> **Not reviewed by a lawyer.** The summaries below are the DataGuard owner's reading of the licence texts
> and are not legal advice. The licence texts themselves are reproduced verbatim.

DataGuard is Copyright (c) 2026 Than Nguyen and DataGuard contributors.

- Releases from **v0.4.0**: GNU General Public License version 3 only (`GPL-3.0-only`), with the additional
  permission in `ADDITIONAL-PERMISSIONS.md` (shipped next to this file), or a separate commercial licence.
  The package `DataGuard.Contracts` is licensed under the MIT licence.
- Releases up to and including **v0.3.0**: MIT licence, permanently (text: `docs/legal/MIT-v0.1.0-v0.3.0.txt`
  in https://github.com/thanhnt-sm/eco_support_net_oracle).

DataGuard's command-line tool, RID archives, container image, NuGet tool package and IDE extensions bundle
the two components below. They are **separately licensed**: the GPL does not apply to them, and their terms
travel with every copy of DataGuard that includes them.

## 1. Oracle.ManagedDataAccess.Core 23.26.301 (Oracle Data Provider for .NET, managed driver)

Licence: Oracle Free Distribution, Hosting, and Use Terms and Conditions, version 1.0 (28 June 2022).
Full text below, copied from the `LICENSE.txt` inside the NuGet package.

What the terms require of someone who redistributes the driver (section "License Rights and Restrictions",
`LICENSE.txt` lines 16-29 of the package):

- redistribute only the **unmodified** driver;
- include a **copy of the Oracle licence** with every distribution (this file is that copy);
- do not charge any additional fee for the distribution or use of the driver as such; distributing it as part
  of a for-fee product that adds substantial additional value is expressly allowed;
- do not remove Oracle's or its licensors' proprietary-rights markings or notices;
- comply with US and applicable export-control and sanctions laws;
- do not cause or permit reverse engineering, disassembly or decompilation of the driver, except as allowed
  by law.

The NuGet package also carries a separate third-party notice file (`info.txt`, Kerberos and related
components) that Oracle ships with the driver; it stays in the NuGet package `Oracle.ManagedDataAccess.Core`.

~~~~text
Your use of this Program is governed by the Oracle Free Distribution, Hosting, and Use Terms and Conditions set forth below, unless you have received this Program (alone or as part of another Oracle product) under an Oracle license agreement (including but not limited to the Oracle Master Agreement), in which case your use of this Program is governed solely by such license agreement with Oracle.

Oracle Free Distribution, Hosting, and Use Terms and Conditions
Definitions
"Oracle" refers to Oracle America, Inc. "You" and "Your" refers to (a) a company or organization (each an "Entity") accessing the Programs, if use of the Programs will be on behalf of such Entity; or (b) an individual accessing the Programs, if use of the Programs will not be on behalf of an Entity. "Program(s)" refers to Oracle software provided by Oracle pursuant to the following terms and any updates, error corrections, and/or Program Documentation provided by Oracle. "Program Documentation" refers to Program user manuals and Program installation manuals, if any. If available, Program Documentation may be delivered with the Programs and/or may be accessed from www.oracle.com/documentation. "Separate Terms" refers to separate license terms that are specified in the Program Documentation, readmes or notice files and that apply to Separately Licensed Technology. "Separately Licensed Technology" refers to Oracle or third party technology that is licensed under Separate Terms and not under the terms of this license.

Separately Licensed Technology
Oracle may provide certain notices to You in Program Documentation, readmes or notice files in connection with Oracle or third party technology provided as or with the Programs. If specified in the Program Documentation, readmes or notice files, such technology will be licensed to You under Separate Terms. Your rights to use Separately Licensed Technology under Separate Terms are not restricted in any way by the terms herein. For clarity, notwithstanding the existence of a notice, third party technology that is not Separately Licensed Technology shall be deemed part of the Programs licensed to You under the terms of this license.

Source Code for Open Source Software
For software that You receive from Oracle in binary form that is licensed under an open source license that gives You the right to receive the source code for that binary, You can obtain a copy of the applicable source code from https://oss.oracle.com/sources/ or http://www.oracle.com/goto/opensourcecode. If the source code for such software was not provided to You with the binary, You can also receive a copy of the source code on physical media by submitting a written request pursuant to the instructions in the "Written Offer for Source Code" section of the latter website.

-------------------------------------------------------------------------------
The following license terms apply to those Programs that are not provided to You under Separate Terms.
License Rights and Restrictions
Oracle grants to You, as a recipient of this Program, a nonexclusive, nontransferable, limited license to, subject to the conditions stated herein, use the unmodified Programs, including, without limitation, for the purposes of:
•	developing, testing, prototyping and demonstrating applications; 
•	running the unmodified Programs for training, personal use, your business operations, and the business operations of third parties;
•	making the unmodified Programs available for use by third parties in your hosted environment and in cloud services; 
•	redistributing unmodified Programs and Programs Documentation under the terms of this License; and
•	copying the unmodified Programs and Program Documentation to the extent reasonably necessary to exercise the license rights granted herein and for backup purposes.
For the purposes of this license, compiling, interpreting or configuring an otherwise unmodified Program as necessary to run the Program shall not be considered modification.

Your license is contingent on Your compliance with the following conditions:
- You include a copy of this license with any distribution by You of the Programs;
- You do not charge your customers, end users, distributees or other third parties any additional fees for the distribution or use of the Programs; however, for clarity, if you comply with the foregoing condition, distribution or use of the Program as part of your for-fee product or service that adds substantial additional value is permitted; 
- You do not remove markings or notices of either Oracle's or a licensor's proprietary rights from the Programs or Program Documentation;
- You comply with all U.S. and applicable export control and economic sanctions laws and regulations that govern Your use of the Programs (including technical data); and
- You do not cause or permit reverse engineering, disassembly or decompilation of the Programs (except as allowed by law) by You nor allow an associated party to do so.
Any source code that may be included in the distribution with the Programs may not be modified, unless such source code is under Separate Terms permitting modification.
Ownership
Oracle or its licensors retain all ownership and intellectual property rights to the Programs.

Information Collection
The Programs' installation and/or auto-update processes, if any, may transmit a limited amount of data to Oracle or its service provider about those processes to help Oracle understand and optimize them. Oracle does not associate the data with personally identifiable information. Refer to Oracle's Privacy Policy at www.oracle.com/privacy.

Disclaimer of Warranties; Limitation of Liability
THE PROGRAMS ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND. ORACLE FURTHER DISCLAIMS ALL WARRANTIES, EXPRESS AND IMPLIED, INCLUDING WITHOUT LIMITATION, ANY IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, OR NONINFRINGEMENT.
IN NO EVENT UNLESS REQUIRED BY APPLICABLE LAW WILL ORACLE BE LIABLE TO YOU FOR DAMAGES, INCLUDING ANY GENERAL, SPECIAL, INCIDENTAL OR CONSEQUENTIAL DAMAGES ARISING OUT OF THE USE OR INABILITY TO USE THE PROGRAM (INCLUDING BUT NOT LIMITED TO LOSS OF DATA OR DATA BEING RENDERED INACCURATE OR LOSSES SUSTAINED BY YOU OR THIRD PARTIES OR A FAILURE OF THE PROGRAM TO OPERATE WITH ANY OTHER PROGRAMS), EVEN IF SUCH HOLDER OR OTHER PARTY HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.

Version 1.0 
Last updated:  28 June 2022
~~~~

## 2. Microsoft.Data.SqlClient.SNI.runtime 7.1.0 (SQL Server networking library)

Licence: Microsoft Software License Terms, "MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY". Full text below, copied
from the `LICENSE.txt` inside the NuGet package.

DataGuard distributes this library as unmodified object code ("Distributable Code"). Points to note
(`LICENSE.txt` section 3.a, lines 12-23 of the package):

- it may only be used as part of an application, not as a standalone distribution;
- distributors and external end users must agree to terms that protect Microsoft at least as much as its
  licence, and distributors must indemnify Microsoft as described there;
- it must not be modified or distributed so that any part of it becomes subject to an "Excluded License"
  (a licence that requires disclosure of source code or a right to modify). The GPL that covers
  DataGuard's own code is **not** applied to this library.

~~~~text
MICROSOFT SOFTWARE LICENSE TERMS

MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY

These license terms are an agreement between you and Microsoft Corporation (or based on where you live, one of its affiliates). They apply to the software named above. The terms also apply to any Microsoft services or updates for the software, except to the extent those have different terms.

IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS BELOW.

1.  INSTALLATION AND USE RIGHTS.
    You may install and use any number of copies of the software to develop and test your applications. 
2.  THIRD PARTY COMPONENTS. The software may include third party components with separate legal notices or governed by other agreements, as may be described in the ThirdPartyNotices file(s) accompanying the software.
3.  ADDITIONAL LICENSING REQUIREMENTS AND/OR USE RIGHTS.
    a. DISTRIBUTABLE CODE.  The software is comprised of Distributable Code. "Distributable Code" is code that you are permitted to distribute in applications you develop if you comply with the terms below.
       i. Right to Use and Distribute.
          * You may copy and distribute the object code form of the software.
          * Third Party Distribution. You may permit distributors of your applications to copy and distribute the Distributable Code as part of those applications.
      ii. Distribution Requirements. For any Distributable Code you distribute, you must
          * use the Distributable Code in your applications and not as a standalone distribution;
          * require distributors and external end users to agree to terms that protect it at least as much as this agreement; and
          * indemnify, defend, and hold harmless Microsoft from any claims, including attorneys' fees, related to the distribution or use of your applications, except to the extent that any claim is based solely on the unmodified Distributable Code.
     iii. Distribution Restrictions. You may not
          * use Microsoft's trademarks in your applications' names or in a way that suggests your applications come from or are endorsed by Microsoft; or
          * modify or distribute the source code of any Distributable Code so that any part of it becomes subject to an Excluded License. An "Excluded License" is one that requires, as a condition of use, modification or distribution of code, that (i) it be disclosed or distributed in source code form; or (ii) others have the right to modify it.
4.  DATA.
    a. Data Collection. Some features in the software may enable collection of data from users of your applications that access or use the software. If you use these features to enable data collection in your applications, you must comply with applicable law, including getting any required user consent, and maintain a prominent privacy policy that accurately informs users about how you use, collect, and share their data. You agree to comply with all applicable provisions of the Microsoft Privacy Statement at [https://go.microsoft.com/fwlink/?LinkId=521839].
5.  SCOPE OF LICENSE. The software is licensed, not sold. This agreement only gives you some rights to use the software. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you may use the software only as expressly permitted in this agreement. In doing so, you must comply with any technical limitations in the software that only allow you to use it in certain ways. You may not
    * work around any technical limitations in the software;
    * reverse engineer, decompile or disassemble the software, or otherwise attempt to derive the source code for the software, except and to the extent required by third party licensing terms governing use of certain open source components that may be included in the software;
    * remove, minimize, block or modify any notices of Microsoft or its suppliers in the software;
    * use the software in any way that is against the law; or
    * share, publish, rent or lease the software, provide the software as a stand-alone offering for others to use, or transfer the software or this agreement to any third party.
6.  EXPORT RESTRICTIONS. You must comply with all domestic and international export laws and regulations that apply to the software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit www.microsoft.com/exporting.  
7.  SUPPORT SERVICES. Because this software is "as is," we may not provide support services for it.
8.  ENTIRE AGREEMENT. This agreement, and the terms for supplements, updates, Internet-based services and support services that you use, are the entire agreement for the software and support services.
9.  APPLICABLE LAW.  If you acquired the software in the United States, Washington law applies to interpretation of and claims for breach of this agreement, and the laws of the state where you live apply to all other claims. If you acquired the software in any other country, its laws apply.
10. CONSUMER RIGHTS; REGIONAL VARIATIONS. This agreement describes certain legal rights. You may have other rights, including consumer rights, under the laws of your state or country. Separate and apart from your relationship with Microsoft, you may also have rights with respect to the party from which you acquired the software. This agreement does not change those other rights if the laws of your state or country do not permit it to do so. For example, if you acquired the software in one of the below regions, or mandatory country law applies, then the following provisions apply to you:
    a) Australia. You have statutory guarantees under the Australian Consumer Law and nothing in this agreement is intended to affect those rights.
    b) Canada. If you acquired this software in Canada, you may stop receiving updates by turning off the automatic update feature, disconnecting your device from the Internet (if and when you re-connect to the Internet, however, the software will resume checking for and installing updates), or uninstalling the software. The product documentation, if any, may also specify how to turn off updates for your specific device or software.
    c) Germany and Austria.
       (i) Warranty. The software will perform substantially as described in any Microsoft materials that accompany it. However, Microsoft gives no contractual guarantee in relation to the software.
       (ii) Limitation of Liability. In case of intentional conduct, gross negligence, claims based on the Product Liability Act, as well as in case of death or personal or physical injury, Microsoft is liable according to the statutory law.
    Subject to the foregoing clause (ii), Microsoft will only be liable for slight negligence if Microsoft is in breach of such material contractual obligations, the fulfillment of which facilitate the due performance of this agreement, the breach of which would endanger the purpose of this agreement and the compliance with which a party may constantly trust in (so-called "cardinal obligations"). In other cases of slight negligence, Microsoft will not be liable for slight negligence
11. DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED "AS-IS." YOU BEAR THE RISK OF USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES OR CONDITIONS. TO THE EXTENT PERMITTED UNDER YOUR LOCAL LAWS, MICROSOFT EXCLUDES THE IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT.
12. LIMITATION ON AND EXCLUSION OF REMEDIES AND DAMAGES. YOU CAN RECOVER FROM MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL, INDIRECT OR INCIDENTAL DAMAGES.
    This limitation applies to (a) anything related to the software, services, content (including code) on third party Internet sites, or third party applications; and (b) claims for breach of contract, breach of warranty, guarantee or condition, strict liability, negligence, or other tort to the extent permitted by applicable law.
    It also applies even if Microsoft knew or should have known about the possibility of the damages. The above limitation or exclusion may not apply to you because your state or country may not allow the exclusion or limitation of incidental, consequential or other damages.
~~~~

## 3. Other third-party components

Other components are used under permissive licences (MIT, Apache-2.0, the PostgreSQL licence, ISC) that
allow redistribution alongside GPL-3.0-only code. They are checked on every CI run against the allow-list in
`scripts/allowed-licences.txt` (`scripts/check-nuget-licences.py`), and each release ships SBOMs listing
them. Visual Studio and Visual Studio Code are the hosts that run the extensions; they are not redistributed
by DataGuard.
