using System.Text;

namespace BMMDL.CodeGen;

/// <summary>
/// Generates PostgreSQL sequence infrastructure for BMMDL auto-numbering
/// </summary>
public class SequenceGenerator
{
    /// <summary>
    /// Generate the bmmdl_sequences table DDL
    /// </summary>
    public string GenerateSequenceTable()
    {
        return @"-- ============================================
-- BMMDL Sequence Management Infrastructure
-- ============================================

CREATE SCHEMA IF NOT EXISTS core;

CREATE TABLE IF NOT EXISTS core.__sequences (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    
    -- Identification
    sequence_name VARCHAR(100) NOT NULL,
    entity_name VARCHAR(200),
    field_name VARCHAR(100),
    
    -- Scope isolation (multi-tenant)
    tenant_id UUID,
    company_id UUID,
    
    -- Reset tracking (for periodic resets)
    year INTEGER,
    month INTEGER,
    day INTEGER,
    
    -- Current value
    current_value INTEGER NOT NULL DEFAULT 0,
    
    -- Configuration (from BMMDL metadata)
    pattern VARCHAR(500),
    start_value INTEGER DEFAULT 1,
    increment INTEGER DEFAULT 1,
    padding INTEGER,
    max_value INTEGER,
    scope VARCHAR(20) DEFAULT 'Company', -- 'Global', 'Tenant', 'Company'
    reset_trigger VARCHAR(20) DEFAULT 'Never', -- 'Never', 'Daily', 'Monthly', 'Yearly'
    
    -- Concurrency control
    version INTEGER DEFAULT 1,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    
    -- Unique constraint based on scope and reset
    UNIQUE (sequence_name, tenant_id, company_id, year, month, day)
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_sequences_lookup 
    ON core.__sequences(sequence_name, tenant_id, company_id);

CREATE INDEX IF NOT EXISTS idx_sequences_scope 
    ON core.__sequences(sequence_name, scope, reset_trigger);

-- Comments for documentation
COMMENT ON TABLE core.__sequences IS 'Stores sequence state for BMMDL auto-numbering with pattern support';
COMMENT ON COLUMN core.__sequences.pattern IS 'Pattern like PO-{company}-{year}{month}-{seq:5}';
COMMENT ON COLUMN core.__sequences.scope IS 'Isolation level: Global, Tenant, or Company';
COMMENT ON COLUMN core.__sequences.reset_trigger IS 'When to reset: Never, Daily, Monthly, Yearly';
";
    }
    
    /// <summary>
    /// Generate the sequence value generation function
    /// </summary>
    public string GenerateSequenceFunction()
    {
        return @"-- ============================================
-- Function: get_next_sequence_value
-- Purpose: Get next sequence value with proper isolation and reset logic
-- ============================================

CREATE OR REPLACE FUNCTION get_next_sequence_value(
    p_sequence_name VARCHAR,
    p_tenant_id UUID,
    p_company_id UUID,
    p_pattern VARCHAR,
    p_scope VARCHAR DEFAULT 'Company',
    p_reset_trigger VARCHAR DEFAULT 'Never'
) RETURNS VARCHAR AS $$
DECLARE
    v_current_value INTEGER;
    v_formatted_value VARCHAR;
    v_year INTEGER := EXTRACT(YEAR FROM CURRENT_DATE);
    v_month INTEGER := EXTRACT(MONTH FROM CURRENT_DATE);
    v_day INTEGER := EXTRACT(DAY FROM CURRENT_DATE);
    v_needs_reset BOOLEAN := FALSE;
    v_existing_record RECORD;
BEGIN
    -- Determine scope conditions
    -- Lock and get current sequence record
    SELECT * INTO v_existing_record
    FROM core.__sequences
    WHERE sequence_name = p_sequence_name
      AND (p_scope = 'Global' OR tenant_id = p_tenant_id)
      AND (p_scope != 'Company' OR company_id = p_company_id)
    ORDER BY id DESC
    LIMIT 1
    FOR UPDATE;
    
    -- Check if reset is needed based on reset_trigger
    IF v_existing_record.id IS NOT NULL THEN
        v_needs_reset := CASE p_reset_trigger
            WHEN 'Daily' THEN 
                v_existing_record.year != v_year OR 
                v_existing_record.month != v_month OR 
                v_existing_record.day != v_day
            WHEN 'Monthly' THEN 
                v_existing_record.year != v_year OR 
                v_existing_record.month != v_month
            WHEN 'Yearly' THEN 
                v_existing_record.year != v_year
            ELSE FALSE
        END;
    END IF;
    
    -- Get next value
    IF v_existing_record.id IS NULL OR v_needs_reset THEN
        -- Create new record or reset
        INSERT INTO core.__sequences (
            sequence_name, tenant_id, company_id,
            year, month, day,
            current_value, pattern, scope, reset_trigger
        ) VALUES (
            p_sequence_name, p_tenant_id, p_company_id,
            v_year, v_month, v_day,
            1, p_pattern, p_scope, p_reset_trigger
        )
        ON CONFLICT (sequence_name, tenant_id, company_id, year, month, day)
        DO UPDATE SET 
            current_value = core.__sequences.current_value + 1,
            version = core.__sequences.version + 1,
            updated_at = NOW()
        RETURNING current_value INTO v_current_value;
        
        IF v_current_value IS NULL THEN
            v_current_value := 1;
        END IF;
    ELSE
        -- Increment existing
        UPDATE core.__sequences
        SET current_value = current_value + 1,
            version = version + 1,
            updated_at = NOW()
        WHERE id = v_existing_record.id
        RETURNING current_value INTO v_current_value;
    END IF;
    
    -- Format value according to pattern
    v_formatted_value := format_sequence_pattern(
        p_pattern, 
        v_current_value, 
        p_company_id, 
        v_year, 
        v_month, 
        v_day
    );
    
    RETURN v_formatted_value;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION get_next_sequence_value IS 'Generate next sequence value with tenant/company isolation and auto-reset support';
";
    }
    
    /// <summary>
    /// Generate the pattern formatting function
    /// </summary>
    public string GeneratePatternFormatterFunction()
    {
        return @"-- ============================================
-- Function: format_sequence_pattern
-- Purpose: Format sequence value according to pattern with tokens
-- ============================================

CREATE OR REPLACE FUNCTION format_sequence_pattern(
    p_pattern VARCHAR,
    p_seq_value INTEGER,
    p_company_id UUID,
    p_year INTEGER,
    p_month INTEGER,
    p_day INTEGER
) RETURNS VARCHAR AS $$
DECLARE
    v_result VARCHAR := p_pattern;
    v_company_code VARCHAR;
    v_padding INTEGER;
    v_seq_match TEXT;
BEGIN
    -- Get company code if needed
    IF p_pattern LIKE '%{company}%' THEN
        SELECT code INTO v_company_code
        FROM companies
        WHERE id = p_company_id;
        
        v_result := REPLACE(v_result, '{company}', COALESCE(v_company_code, 'UNKNOWN'));
    END IF;
    
    -- Replace date tokens
    v_result := REPLACE(v_result, '{year}', p_year::TEXT);
    v_result := REPLACE(v_result, '{month}', LPAD(p_month::TEXT, 2, '0'));
    v_result := REPLACE(v_result, '{day}', LPAD(p_day::TEXT, 2, '0'));
    
    -- Handle {seq:N} with padding
    v_seq_match := substring(v_result from '\{seq:(\d+)\}');
    IF v_seq_match IS NOT NULL THEN
        v_padding := v_seq_match::INTEGER;
        v_result := regexp_replace(
            v_result, 
            '\{seq:\d+\}', 
            LPAD(p_seq_value::TEXT, v_padding, '0')
        );
    ELSE
        -- Handle {seq} without padding
        v_result := REPLACE(v_result, '{seq}', p_seq_value::TEXT);
    END IF;
    
    RETURN v_result;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION format_sequence_pattern IS 'Format sequence pattern with tokens: {company}, {year}, {month}, {day}, {seq:N}';
";
    }
    
    /// <summary>
    /// Generate all sequence infrastructure SQL
    /// </summary>
    public string GenerateAllSequenceInfrastructure()
    {
        var sb = new StringBuilder();
        
        sb.AppendLine(GenerateSequenceTable());
        sb.AppendLine();
        sb.AppendLine(GenerateSequenceFunction());
        sb.AppendLine();
        sb.AppendLine(GeneratePatternFormatterFunction());
        
        return sb.ToString();
    }
}
