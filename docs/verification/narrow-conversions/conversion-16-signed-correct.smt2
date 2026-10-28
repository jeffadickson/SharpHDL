(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (let ((a!1 (= (ite (bvult (bvand input_0 #x0000ffff) #x00008000) #b1 #b0) #b1)))
(let ((a!2 (= ((_ sign_extend 16) ((_ extract 15 0) input_0))
              (ite a!1
                   (bvand input_0 #x0000ffff)
                   (bvsub (bvand input_0 #x0000ffff) #x00010000)))))
  (not a!2))))

(check-sat)
