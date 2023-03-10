Private formRedaction As clsRedaction
Private formDoc As Document
Private formDebug As Boolean
'@TODO: add switch in UserForm
Private ppShowLogLength As Integer
'@TODO: add switch in UserForm
Private pMaxMultipleHighlightChars As Long

Private Sub CommandButton2_Click()
    ' debug setting! Turn False in production!
    formDebug = False
    ppShowLogLength = 50
    pMaxMultipleHighlightChars = 500

    ' Starting
    formRedaction.resetRedactedColorCounts
    log_text "Starting Redaction at " & Time
    If formDebug = False Then
        Application.ScreenUpdating = False
    End If
   
    Dim startRedactionPage As Integer
    startRedactionPage = CInt(TB_startRedactionPage.text)
   
    If Me.LB_colorsToRedact.ListCount = 0 Then
        log_text ("please load colors or start macro from redactionMacro")
        GoTo EndRedaction
    End If
 
    ' XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
    ' XXXXXXXXXXXXXX get user selected colors
    ' XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
    build_user_color_selection_array
    userColorSelectionArray = formRedaction.getToRedactColorsAsIndex
    redactStoryRangeArray = formRedaction.getRedactStoryRangeAsIntArray
   
    If UBound(userColorSelectionArray) = 0 And userColorSelectionArray(0) = "" Then
        log_text "***** FATAL *****" & vbCrLf & "No colors selected. Use mouse to select multiple. Exiting..."
        GoTo EndRedaction
    End If
   
    Dim currentHighlightColor As String
    Dim myRange As Range
   
    ' in stories, check if the color is in the array the user has asked us, if so, replace
    For i = 0 To UBound(redactStoryRangeArray)
        Set currentPosition = formDoc.StoryRanges(redactStoryRangeArray(i))
        reset_search_parameters currentPosition
        With currentPosition.Find
        .Highlight = True
            Do While .Execute(FindText:="", Forward:=True, Format:=True) = True
                ' Start redaction from page 2?
                If currentPosition.Information(wdActiveEndPageNumber) < startRedactionPage Then
                    GoTo skipReplace
                End If

                Set myRange = currentPosition               
                currentHighlightColor = LTrim(Str(currentPosition.HighlightColorIndex))

                If is_in_array(currentHighlightColor, userColorSelectionArray) = True Then
                    ' replace!
                    check_and_redact_range myRange
 
                ElseIf currentHighlightColor = "9999999" Then
                    If myRange.storyType <> wdMainTextStory Then
                        ' save location of multiple highlights
                        multipleHighlightsText = multipleHighlightsText & "> Page " & myRange.Information(wdActiveEndPageNumber) & ": " & Left(myRange.text, pShowLogLength) & vbCrLf
                        GoTo skipReplace
                    Else
                        ' multiple highlights detected, find begining and end of correct highlight colors
                        go_through_chars_to_redact_multiple_highlights myRange
                        ' or just add log and skipReplace:
                        ' @TODO: Add switch in UserForm
                        'multipleHighlightsText = multipleHighlightsText & "Page " & currentPosition.Information(wdActiveEndPageNumber) & ": " & Left(currentPosition.text, 50) & vbCrLf
                        'GoTo skipReplace
                    End If
                End If
 
skipReplace:
                currentPosition.Collapse wdCollapseEnd
                myRange.Collapse wdCollapseEnd
            Loop
        End With
    Next
   
    If multipleHighlightsText <> "" Then
        log_text ("***** Warning *****" & vbCrLf & "Manually review multiple highligted text in text boxes:" & vbCrLf & vbCrLf & multipleHighlightsText)
    End If
   
    ' Save and Finish
    Dim fileSuffix As String
    fileSuffix = TB_fileSuffix.text
    ' also sets the active document / formDoc to the original file!
    save_file fileSuffix
   
    send_finish_log fileSuffix
   
EndRedaction:
    log_text "Finished at " & Time
    Application.ScreenUpdating = True
    formDoc.Activate
    Me.Show vbModeless
End Sub
 
Private Sub go_through_chars_to_redact_multiple_highlights(currentRange As Variant)
    Dim replaceStartPos As Long
    Dim prevHighlightColor As String
    Dim currentHighlightColor As String
    Dim myRange As Range
 
    userColorSelectionArray = formRedaction.getToRedactColorsAsIndex
 
    replaceStartPos = 0
    prevHighlightColor = ""
    Set activeStoryRange = currentRange
   
   '@TODO: add switch in UserForm
   Dim pMaxMultipleHighlightChars As Long
   pMaxMultipleHighlightChars = 500
    If activeStoryRange.Characters.Count > pMaxMultipleHighlightChars Then
        log_text "***** Warning *****" & vbCrLf & "Text with multiple highlights is longer than " & pMaxMultipleHighlightChars & " chars. Skip, review manually" _ 
            & vbCrLf & "> Page " & activeStoryRange.Information(wdActiveEndPageNumber) & " starting with: " & Left(activeStoryRange.text, pShowLogLength)
        Exit Sub
    End If
   
    For Each Char In activeStoryRange.Characters
        currentHighlightColor = LTrim(Str(Char.HighlightColorIndex))
       
        ' Char should be replaced
        If is_in_array(currentHighlightColor, userColorSelectionArray) = True Then
            ' no replace start pos, this is the first char of the highlighted text
            If replaceStartPos = 0 Then
                replaceStartPos = Char.Start
            ElseIf currentHighlightColor <> prevHighlightColor Then
            ' not the first character, but colors changed to another to be replaced Character
                Set myRange = formDoc.StoryRanges(activeStoryRange.storyType)
                myRange.Start = replaceStartPos
                myRange.End = Char.Start
                check_and_redact_range myRange
                ' this is set to zero, because we're skipping all the characters from the replaced string, and will pass by the replaceStartPos = 0 if clause
                replaceStartPos = Char.End - 1
            End If
            prevHighlightColor = currentHighlightColor
        Else
            ' if Char is not highligted AND (but the prev chars where highlighted / there was a replaceStartPos), then replace the string
            If (replaceStartPos <> 0) Then
                Set myRange = formDoc.StoryRanges(activeStoryRange.storyType)
                myRange.Start = replaceStartPos
                myRange.End = Char.Start
                check_and_redact_range myRange
                replaceStartPos = 0
            End If
        End If
    Next Char
   
    If (replaceStartPos <> 0) Then
        Set myRange = formDoc.StoryRanges(activeStoryRange.storyType)
        myRange.Start = replaceStartPos
        myRange.End = currentRange.End
        check_and_redact_range myRange
        replaceStartPos = 0
    End If
 
End Sub
 
' !!! RECURSIVE FUNCTION !!!
'
' this will go check if in the current range there is a footnote or field reference.
' - if so, it will split the range and call itself / repeat until
' - if there is not footnote or field ref inside the range
' - check if this range contains a target of a field ref -> if so alert user and do not redact
' - if all is well finally redact!
Private Function check_and_redact_range(currentRange As Range, Optional depth As Integer = 1)
 
    debug_true currentRange.text

    If depth > 2 Then
        log_text "***** Warning *****" & vbCrLf & "Recursive Function reached depth 3. Skip, review manually" & vbCrLf & _ 
            "> Page " & currentRange.Information(wdActiveEndPageNumber) & " starting with: " & Left(currentRange.text, pShowLogLength)
        Exit Function
    End If

    ' check if bookmark target is in current range - currently: Exit
    ' @TODO: we want to be able to split the range, once we don't need to loop through all fields anymore
    If (check_for_bookmark_target(currentRange) = True) And (depth = 1) Then
        log_text "***** Warning *****" & vbCrLf & "Trying to redact target of a cross refernce. Skip, review manually" & vbCrLf & _ 
            "> Page " & currentRange.Information(wdActiveEndPageNumber) & " starting with: " & Left(currentRange.text, pShowLogLength)
        Exit Function
    End If

    ' Change range if ASCII Control Characters (00,01,02,03,04,09,10,12,13, or under 32) are found:
    If (Asc(currentRange.Characters.First.text) < 32) Or (Asc(currentRange.Characters.Last.text) < 32) Then
        If Len(currentRange.text) = 1 Then
            Exit Function
        End If
        ' check for starter and ending empty spaces:
        If (Asc(currentRange.Characters.First.text) < 32) Then
            currentRange.Start = currentRange.Start + 1
            debug_true "Removed first char of range (" & currentRange.Start & "/" & currentRange.End & ")"
        End If
        If (Asc(currentRange.Characters.Last.text) < 32) Then
            currentRange.End = currentRange.End - 1
            debug_true "Removed first char of range (" & currentRange.Start & "/" & currentRange.End & ")"
        End If
    End If
 
    ' get the current highlight color
    ' we need this for counting
    Dim highlightColor As Long
    highlightColor = currentRange.Characters(1).HighlightColorIndex
   
    ' getting redaction text
    Dim redactionText As String
    redactionText = TB_redactionText.text

    Dim footnoteOrFieldFound As Boolean
    footnoteOrFieldFound = False
   
    ' ********************************************************
    '  Do we have a footnote marker in this range, and are we in the main text story?
    '    if so, replace around it.
    ' ********************************************************
    If currentRange.storyType = wdMainTextStory Then
        For Each Footnote In formDoc.Footnotes
            ' we can break out, if the footnote liste is ordered & the end of our range is less than the start of the next reference
            ' it should be ordered: https://learn.microsoft.com/en-us/office/vba/api/word.footnotes (last example)
            If (currentRange.End < Footnote.Reference.Start) Then
                Exit For
            End If

            If (currentRange.Start <= Footnote.Reference.Start) And (Footnote.Reference.End <= currentRange.End) Then
                Dim firstRange As Range
                Dim secondRange As Range
               
                footnoteOrFieldFound = True
                'first range could have another field that comes later, as array is not ordered!
                Set firstRange = formDoc.StoryRanges(currentRange.storyType)
                firstRange.Start = currentRange.Start
                firstRange.End = Footnote.Reference.Start
               
                firstRange.text = redactionText
                formRedaction.addRedactedCountByColor LTrim(Str(highlightColor))
               
                ' second Range: define and run check again
                Set secondRange = formDoc.StoryRanges(currentRange.storyType)
                secondRange.Start = Footnote.Reference.End
                secondRange.End = currentRange.End
                check_and_redact_range secondRange, depth + 1
            End If
        Next
    End If
   
    ' ********************************************************
    '  Go through cross references and see if we're in one
    '    if so, replace around it
    ' ********************************************************
    For Each field In currentRange.Fields
        ' IMPORTANT: fields start at field.Code.start and end at field.Result.End
        ' These are not ordered
        If (currentRange.storyType = field.Result.storyType) And (currentRange.Start <= field.Code.Start) And (field.Result.End <= currentRange.End) Then
            footnoteOrFieldFound = True
            ' there are fields in this range:
            Set firstRange = formDoc.StoryRanges(currentRange.storyType)
            firstRange.Start = currentRange.Start
            ' !!! this leaves the start code!
            firstRange.End = field.Code.Start - 1
 
            firstRange.text = redactionText
            formRedaction.addRedactedCountByColor LTrim(Str(highlightColor))
           
            Set secondRange = formDoc.StoryRanges(currentRange.storyType)
            secondRange.Start = field.Result.End + 1
            secondRange.End = currentRange.End
 
            check_and_redact_range secondRange, depth + 1
        End If
    Next
 
    ' ********************************************************
    '  Not a cross reference target, not a footnote marker, not a reference field
    '    so we can redact it
    ' ********************************************************
    on error goto skipAndLog
    If footnoteOrFieldFound = False Then
        currentRange.text = redactionText
        formRedaction.addRedactedCountByColor LTrim(Str(highlightColor))
        Exit Function
    Else
        Exit Function
    End If
skipAndLog:
    log_text "***** ERROR *****" & vbCrLf & "Could not redact. Skip, review manually" & vbCrLf & "> Page " & currentRange.Information(wdActiveEndPageNumber) & " starting with: " & Left(currentRange.text, pShowLogLength)
End Function

' Test if a bookmark target is in current range
' currently only checks for "REF" and not other targets!
' @TODO: on every call we're looping through all the fields. Better: make an index of all fields that are relevant (with Ref) and only loop through it here (check in range!)
Private Function check_for_bookmark_target (currentRange as Range) as Boolean
    ' check if range is target of a field
    redactStoryRangeArray = formRedaction.getRedactStoryRangeAsIntArray
    Dim newStoryRange As Range
    Dim bookmarkRange As Range
    Dim vSt1 As String
    For i = 0 To UBound(redactStoryRangeArray)
        Set newStoryRange = formDoc.StoryRanges(redactStoryRangeArray(i))
        For Each field In newStoryRange.Fields
            vSt1 = field.Code
            If (Len(vSt1) > 1) Then
                If (Split(vSt1, " ")(1) = "REF") Then
                    vSt1 = Split(vSt1, " ")(2)
                    If formDoc.Bookmarks.Exists(vSt1) Then
                        Set bookmarkRange = formDoc.Bookmarks(vSt1).Range
                        If (currentRange.storyType = bookmarkRange.storyType) And (currentRange.Start <= bookmarkRange.Start) And (bookmarkRange.End <= currentRange.End) Then
                            check_for_bookmark_target = true
                        End If
                    End If
                End If
            End If
        Next field
    Next i
    check_for_bookmark_target = False
End Function
 
Private Function reset_search_parameters(oRng As Variant)
  With oRng.Find
    .ClearFormatting
    .Replacement.ClearFormatting
    .text = ""
    .Replacement.text = ""
    .Forward = True
    .Wrap = wdFindStop
    .Format = False
    .MatchCase = False
    .MatchWholeWord = False
    .MatchWildcards = False
    .MatchSoundsLike = False
    .MatchAllWordForms = False
    .Execute
  End With
End Function
 
Private Function save_file(fileSuffix As String)
    ' XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
    ' XXXXXXXXXXXXXX GET Filename
    ' XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
    originalDocumentName = formDoc.Name
   
    intPos = InStrRev(originalDocumentName, ".")
   
    strPath = formDoc.Path
    If Right(strPath, 1) <> "\" Then
        strPath = strPath & "\"
    End If
   
    ' build file name, either from user color selection or if the user has provided a file name use that one.
    newDocumentName = Left(originalDocumentName, intPos - 1) & "-" & Left(fileSuffix, 10) & ".docx"
       
    ' saving new document
    formDoc.SaveAs2 fileName:=strPath & newDocumentName, FileFormat:=wdFormatDocumentDefault
   
    ' open original
    Documents.Open(strPath & originalDocumentName).Activate
    Set formDoc = ActiveDocument
End Function
 
Private Sub send_finish_log(fileSuffix As String)
    ' trim the last comma and add a point
    sColorConcat = formRedaction.getRedactedColorsWithCount & ". Total redactions: " & formRedaction.getTotalRedactedCount
   
    log_text "***************************************************************"
    log_text "Redacted Version (" & fileSuffix & ") - redacted Colors: " & sColorConcat
    log_text "***************************************************************"
 
End Sub
 
Private Sub UserForm_Initialize()
    log_text "Initializing Form..."
    log_text "Please note: page numbers are only an indication, the real page numbers will be lower as redactions will shorten the document!"
End Sub
 
Property Set setformRedaction(ByRef redaction As clsRedaction)
    Set formRedaction = redaction
End Property
 
Property Get getformRedaction() As clsRedaction
    Set getformRedaction = formRedaction
End Property
 
Property Set setFormDoc(ByRef doc As Document)
    Set formDoc = doc
End Property
 
Property Get getFormDoc() As Document
    Set getDoc = formDoc
End Property
 
Public Sub build_user_color_selection_array()
    Dim selectedColorText As String
    Dim toRedactColorsAsIndex() As String
    Dim toRedactColorsAsName() As String
   
    ReDim toRedactColorsAsIndex(Me.LB_colorsToRedact.ListCount - 1)
    ReDim toRedactColorsAsName(Me.LB_colorsToRedact.ListCount - 1)
   
    Dim counter As Integer
    Dim color As Integer
   
    counter = 0
    For color = 0 To Me.LB_colorsToRedact.ListCount - 1
        If Me.LB_colorsToRedact.Selected(color) = True Then
            selectedColorText = Me.LB_colorsToRedact.List(color)
           
            Select Case selectedColorText
                Case "black"
                    toRedactColorsAsIndex(counter) = wdBlack
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "blue"
                    toRedactColorsAsIndex(counter) = wdBlue
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "turquoise"
                    toRedactColorsAsIndex(counter) = wdTurquoise
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "bGreen"
                    toRedactColorsAsIndex(counter) = wdBrightGreen
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "pink"
                    toRedactColorsAsIndex(counter) = wdPink
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "red"
                    toRedactColorsAsIndex(counter) = wdRed
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "yellow"
                    toRedactColorsAsIndex(counter) = wdYellow
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "white"
                    toRedactColorsAsIndex(counter) = wdWhite
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "dBlue"
                    toRedactColorsAsIndex(counter) = wdDarkBlue
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "teal"
                    toRedactColorsAsIndex(counter) = wdTeal
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "green"
                    toRedactColorsAsIndex(counter) = wdGreen
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "violet"
                    toRedactColorsAsIndex(counter) = wdViolet
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "dRed"
                    toRedactColorsAsIndex(counter) = wdDarkRed
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "dYellow"
                    toRedactColorsAsIndex(counter) = wdDarkYellow
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "gray50"
                    toRedactColorsAsIndex(counter) = wdGray50
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
                Case "gray25"
                    toRedactColorsAsIndex(counter) = wdGray25
                    toRedactColorsAsName(counter) = selectedColorText
                    counter = counter + 1
            End Select
        End If
       
    Next color
   
    ReDim Preserve toRedactColorsAsIndex(counter - 1)
    ReDim Preserve toRedactColorsAsName(counter - 1)
   
    Dim fRedaction As clsRedaction
    Set fRedaction = Me.getformRedaction
   
    fRedaction.setRedactColors toRedactColorsAsIndex, toRedactColorsAsName, counter - 1
End Sub
 
Private Function log_text(text As String)
    logBox.text = logBox.text & text & vbCrLf & vbCrLf
End Function

Private Function debug_true (text As String, Optional debugBool As Boolean = True)
    If formDebug = debugBool Then
        Debug.Print text
    End If
End Function